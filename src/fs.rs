use anyhow::Result;
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
