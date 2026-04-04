use anyhow::{bail, Result};
use russh::client;
use russh_sftp::client::SftpSession;
use std::path::Path;

#[derive(Clone, Debug)]
pub struct FileEntry {
    pub name: String,
    pub is_dir: bool,
    pub size: u64,
}

impl FileEntry {
    pub fn parent() -> Self {
        FileEntry {
            name: "..".to_string(),
            is_dir: true,
            size: 0,
        }
    }
}

/// List files in a local directory.
pub fn list_local(dir: &Path) -> Result<Vec<FileEntry>> {
    let mut entries = vec![FileEntry::parent()];

    let read_dir = std::fs::read_dir(dir)?;
    for entry in read_dir {
        let entry = entry?;
        let metadata = entry.metadata()?;
        entries.push(FileEntry {
            name: entry.file_name().to_string_lossy().to_string(),
            is_dir: metadata.is_dir(),
            size: metadata.len(),
        });
    }

    sort_entries(&mut entries);
    Ok(entries)
}

/// List files in a remote directory via SFTP.
pub async fn list_remote(sftp: &SftpSession, dir: &str) -> Result<Vec<FileEntry>> {
    let mut entries = vec![FileEntry::parent()];

    let dir_entries = sftp.read_dir(dir).await?;
    for entry in dir_entries {
        let name = entry.file_name();
        if name == "." || name == ".." {
            continue;
        }

        let is_dir = entry.file_type().is_dir();
        let size = entry.metadata().len();

        entries.push(FileEntry {
            name,
            is_dir,
            size,
        });
    }

    sort_entries(&mut entries);
    Ok(entries)
}

/// Delete files and folders on the remote host via SFTP.
pub async fn delete_remote(
    sftp: &SftpSession,
    base_dir: &str,
    entries: &[FileEntry],
) -> Result<usize> {
    let base = base_dir.trim_end_matches('/');
    let mut count = 0;
    for entry in entries {
        if entry.name == ".." {
            continue;
        }
        let path = format!("{}/{}", base, entry.name);
        if entry.is_dir {
            delete_remote_recursive(sftp, &path).await?;
        } else {
            sftp.remove_file(&path).await?;
        }
        count += 1;
    }
    Ok(count)
}

async fn delete_remote_recursive(sftp: &SftpSession, path: &str) -> Result<()> {
    let children = sftp.read_dir(path).await?;
    for child in children {
        let name = child.file_name();
        if name == "." || name == ".." {
            continue;
        }
        let child_path = format!("{}/{}", path, name);
        if child.file_type().is_dir() {
            Box::pin(delete_remote_recursive(sftp, &child_path)).await?;
        } else {
            sftp.remove_file(&child_path).await?;
        }
    }
    sftp.remove_dir(path).await?;
    Ok(())
}

/// Copy files/folders on the remote host via `cp -r`.
pub async fn copy_remote<H: client::Handler>(
    handle: &client::Handle<H>,
    base_dir: &str,
    entries: &[FileEntry],
    dst_dir: &str,
) -> Result<usize> {
    let base = base_dir.trim_end_matches('/');
    let dst = dst_dir.trim_end_matches('/');
    let mut count = 0;
    for entry in entries {
        if entry.name == ".." {
            continue;
        }
        let src_path = format!("{}/{}", base, entry.name);
        let dst_path = format!("{}/{}", dst, entry.name);
        let cmd = format!(
            "cp -r {} {}",
            shell_escape(&src_path),
            shell_escape(&dst_path)
        );
        exec_remote_cmd(handle, &cmd).await?;
        count += 1;
    }
    Ok(count)
}

/// Move files/folders on the remote host via `mv`.
pub async fn move_remote<H: client::Handler>(
    handle: &client::Handle<H>,
    base_dir: &str,
    entries: &[FileEntry],
    dst_dir: &str,
) -> Result<usize> {
    let base = base_dir.trim_end_matches('/');
    let dst = dst_dir.trim_end_matches('/');
    let mut count = 0;
    for entry in entries {
        if entry.name == ".." {
            continue;
        }
        let src_path = format!("{}/{}", base, entry.name);
        let dst_path = format!("{}/{}", dst, entry.name);
        let cmd = format!(
            "mv {} {}",
            shell_escape(&src_path),
            shell_escape(&dst_path)
        );
        exec_remote_cmd(handle, &cmd).await?;
        count += 1;
    }
    Ok(count)
}

/// Merge the contents of multiple remote folders into a single new folder.
/// Copies the *contents* of each folder (not the folder itself) into `new_folder`.
/// If `delete_originals` is true, removes each source folder after copying.
pub async fn merge_folders_remote<H: client::Handler>(
    handle: &client::Handle<H>,
    base_dir: &str,
    folders: &[FileEntry],
    new_folder: &str,
    delete_originals: bool,
) -> Result<usize> {
    let base = base_dir.trim_end_matches('/');
    let dst = new_folder.trim_end_matches('/');
    let mut count = 0;
    for entry in folders {
        if entry.name == ".." || !entry.is_dir {
            continue;
        }
        let src_dir = format!("{}/{}", base, entry.name);
        let src_contents = format!("{}/.", src_dir);
        let dst_slash = format!("{}/", dst);
        let cmd = if delete_originals {
            format!(
                "cp -r {} {} && rm -rf {}",
                shell_escape(&src_contents),
                shell_escape(&dst_slash),
                shell_escape(&src_dir)
            )
        } else {
            format!("cp -r {} {}", shell_escape(&src_contents), shell_escape(&dst_slash))
        };
        exec_remote_cmd(handle, &cmd).await?;
        count += 1;
    }
    Ok(count)
}

async fn exec_remote_cmd<H: client::Handler>(
    handle: &client::Handle<H>,
    cmd: &str,
) -> Result<()> {
    let mut channel = handle.channel_open_session().await?;
    channel.exec(true, cmd).await?;
    let mut status = None;
    // Don't break on Eof/Close — ExitStatus can arrive after them.
    // Only break on None (channel fully closed).
    loop {
        match channel.wait().await {
            Some(russh::ChannelMsg::ExitStatus { exit_status }) => {
                status = Some(exit_status);
            }
            Some(_) => {}
            None => break,
        }
    }
    if let Some(code) = status {
        if code != 0 {
            bail!("Remote command failed (exit {}): {}", code, cmd);
        }
    }
    Ok(())
}

fn shell_escape(s: &str) -> String {
    format!("'{}'", s.replace('\'', "'\\''"))
}

fn sort_entries(entries: &mut [FileEntry]) {
    if entries.len() <= 1 {
        return;
    }
    // Keep ".." at index 0, sort the rest: directories first, then alphabetical
    entries[1..].sort_by(|a, b| {
        b.is_dir
            .cmp(&a.is_dir)
            .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
    });
}
