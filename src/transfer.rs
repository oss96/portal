use anyhow::{bail, Context, Result};
use russh_sftp::client::SftpSession;
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use tokio::io::{AsyncReadExt, AsyncWriteExt};

/// Shared progress state updated during transfers.
#[derive(Clone, Default)]
pub struct TransferProgress {
    pub current_file: String,
    pub files_done: usize,
    pub files_total: usize,
    pub bytes_done: u64,
    pub bytes_total: u64,
    pub started_at: Option<std::time::Instant>,
}

const CHUNK_SIZE: usize = 1024 * 1024; // 1 MB

// ── SCP Download (scp -r -f) ──────────────────────────────────────────

/// Download files/folders from remote via SCP protocol.
/// `stream` must be a channel where `scp -r -f <path>` was exec'd.
pub async fn scp_download<S>(
    stream: &mut S,
    local_base: &Path,
    progress: &Arc<Mutex<TransferProgress>>,
    cancel: &Arc<AtomicBool>,
) -> Result<usize>
where
    S: AsyncReadExt + AsyncWriteExt + Unpin,
{
    // Send initial ACK to start the protocol
    stream.write_all(&[0u8]).await?;

    let mut dir_stack = vec![local_base.to_path_buf()];
    let mut file_count = 0usize;
    let mut line_buf = Vec::with_capacity(512);

    loop {
        if cancel.load(Ordering::Relaxed) {
            bail!("Cancelled");
        }

        // Read one byte to determine message type
        let mut byte = [0u8; 1];
        match stream.read(&mut byte).await {
            Ok(0) => break, // EOF — transfer complete
            Ok(_) => {}
            Err(_) => break,
        }

        match byte[0] {
            b'C' => {
                // File entry: C<mode> <size> <name>\n
                line_buf.clear();
                read_line_into(stream, &mut line_buf).await?;
                let line = String::from_utf8_lossy(&line_buf);
                let parts: Vec<&str> = line.trim().splitn(3, ' ').collect();
                if parts.len() < 3 {
                    bail!("Invalid SCP C line: {}", line);
                }
                let size: u64 = parts[1].parse().context("Invalid file size in SCP")?;
                let name = parts[2];

                let current_dir = dir_stack.last().unwrap();
                let local_path = current_dir.join(name);

                if let Ok(mut p) = progress.lock() {
                    p.current_file = name.to_string();
                }

                // ACK
                stream.write_all(&[0u8]).await?;

                // Read exactly `size` bytes
                let mut remaining = size;
                if let Some(parent) = local_path.parent() {
                    std::fs::create_dir_all(parent).ok();
                }
                let mut file = tokio::fs::File::create(&local_path).await?;
                let mut buf = vec![0u8; CHUNK_SIZE];

                while remaining > 0 {
                    if cancel.load(Ordering::Relaxed) {
                        bail!("Cancelled");
                    }
                    let to_read = (remaining as usize).min(buf.len());
                    let n = stream.read(&mut buf[..to_read]).await?;
                    if n == 0 {
                        bail!("Unexpected EOF during SCP file transfer");
                    }
                    file.write_all(&buf[..n]).await?;
                    remaining -= n as u64;

                    if let Ok(mut p) = progress.lock() {
                        p.bytes_done += n as u64;
                    }
                }

                // Read trailing \0 from server
                stream.read_exact(&mut byte).await?;
                // ACK
                stream.write_all(&[0u8]).await?;

                file_count += 1;
                if let Ok(mut p) = progress.lock() {
                    p.files_done = file_count;
                }
            }
            b'D' => {
                // Directory entry: D<mode> 0 <name>\n
                line_buf.clear();
                read_line_into(stream, &mut line_buf).await?;
                let line = String::from_utf8_lossy(&line_buf);
                let parts: Vec<&str> = line.trim().splitn(3, ' ').collect();
                if parts.len() < 3 {
                    bail!("Invalid SCP D line: {}", line);
                }
                let name = parts[2];

                let current_dir = dir_stack.last().unwrap();
                let new_dir = current_dir.join(name);
                std::fs::create_dir_all(&new_dir)?;
                dir_stack.push(new_dir);

                // ACK
                stream.write_all(&[0u8]).await?;
            }
            b'E' => {
                // End directory
                read_line_into(stream, &mut line_buf).await?; // consume \n
                if dir_stack.len() > 1 {
                    dir_stack.pop();
                }
                // ACK
                stream.write_all(&[0u8]).await?;
            }
            0x01 => {
                // Warning
                line_buf.clear();
                read_line_into(stream, &mut line_buf).await?;
                let msg = String::from_utf8_lossy(&line_buf);
                eprintln!("SCP warning: {}", msg);
                // ACK and continue
                stream.write_all(&[0u8]).await?;
            }
            0x02 => {
                // Fatal error
                line_buf.clear();
                read_line_into(stream, &mut line_buf).await?;
                let msg = String::from_utf8_lossy(&line_buf);
                bail!("SCP error: {}", msg);
            }
            _ => {
                // Unknown, try to read rest of line and continue
                line_buf.clear();
                read_line_into(stream, &mut line_buf).await?;
            }
        }
    }

    Ok(file_count)
}

// ── SCP Upload (scp -r -t) ────────────────────────────────────────────

/// Upload files/folders to remote via SCP protocol.
/// `stream` must be a channel where `scp -r -t <path>` was exec'd.
pub async fn scp_upload<S>(
    stream: &mut S,
    local_path: &Path,
    progress: &Arc<Mutex<TransferProgress>>,
    cancel: &Arc<AtomicBool>,
) -> Result<usize>
where
    S: AsyncReadExt + AsyncWriteExt + Unpin,
{
    // Read initial ACK from server
    let mut ack = [0u8; 1];
    stream.read_exact(&mut ack).await?;
    if ack[0] != 0 {
        bail!("SCP server rejected transfer (initial ACK = {})", ack[0]);
    }

    let count = scp_upload_recursive(stream, local_path, progress, cancel).await?;

    Ok(count)
}

async fn scp_upload_recursive<S>(
    stream: &mut S,
    local_path: &Path,
    progress: &Arc<Mutex<TransferProgress>>,
    cancel: &Arc<AtomicBool>,
) -> Result<usize>
where
    S: AsyncReadExt + AsyncWriteExt + Unpin,
{
    let mut count = 0usize;

    if local_path.is_dir() {
        let dir_name = local_path
            .file_name()
            .unwrap_or_default()
            .to_string_lossy();

        // Send D entry
        let header = format!("D0755 0 {}\n", dir_name);
        stream.write_all(header.as_bytes()).await?;
        read_ack(stream).await?;

        let mut entries: Vec<_> = std::fs::read_dir(local_path)?
            .filter_map(|e| e.ok())
            .collect();
        entries.sort_by_key(|e| e.file_name());

        for entry in entries {
            if cancel.load(Ordering::Relaxed) {
                bail!("Cancelled");
            }
            count += Box::pin(scp_upload_recursive(
                stream,
                &entry.path(),
                progress,
                cancel,
            ))
            .await?;
        }

        // Send E to end directory
        stream.write_all(b"E\n").await?;
        read_ack(stream).await?;
    } else if local_path.is_file() {
        let metadata = std::fs::metadata(local_path)?;
        let size = metadata.len();
        let name = local_path
            .file_name()
            .unwrap_or_default()
            .to_string_lossy();

        if let Ok(mut p) = progress.lock() {
            p.current_file = name.to_string();
        }

        // Send C entry
        let header = format!("C0644 {} {}\n", size, name);
        stream.write_all(header.as_bytes()).await?;
        read_ack(stream).await?;

        // Send file data
        let mut file = tokio::fs::File::open(local_path).await?;
        let mut buf = vec![0u8; CHUNK_SIZE];
        let mut remaining = size;

        while remaining > 0 {
            if cancel.load(Ordering::Relaxed) {
                bail!("Cancelled");
            }
            let to_read = (remaining as usize).min(buf.len());
            let n = file.read(&mut buf[..to_read]).await?;
            if n == 0 {
                break;
            }
            stream.write_all(&buf[..n]).await?;
            remaining -= n as u64;

            if let Ok(mut p) = progress.lock() {
                p.bytes_done += n as u64;
            }
        }

        // Send trailing \0
        stream.write_all(&[0u8]).await?;
        read_ack(stream).await?;

        count += 1;
        if let Ok(mut p) = progress.lock() {
            p.files_done = count;
        }
    }

    Ok(count)
}

// ── SCP Helpers ────────────────────────────────────────────────────────

async fn read_line_into<S: AsyncReadExt + Unpin>(stream: &mut S, buf: &mut Vec<u8>) -> Result<()> {
    buf.clear();
    loop {
        let mut byte = [0u8; 1];
        let n = stream.read(&mut byte).await?;
        if n == 0 {
            break;
        }
        if byte[0] == b'\n' {
            break;
        }
        buf.push(byte[0]);
    }
    Ok(())
}

async fn read_ack<S: AsyncReadExt + Unpin>(stream: &mut S) -> Result<()> {
    let mut byte = [0u8; 1];
    stream.read_exact(&mut byte).await?;
    if byte[0] != 0 {
        bail!("SCP server error (ACK = {})", byte[0]);
    }
    Ok(())
}

// ── Total Size Helpers ─────────────────────────────────────────────────

/// Compute total bytes for a remote path via SFTP metadata.
pub async fn remote_total_bytes(sftp: &SftpSession, remote_path: &str, is_dir: bool) -> u64 {
    if !is_dir {
        return sftp
            .metadata(remote_path)
            .await
            .map(|m| m.len())
            .unwrap_or(0);
    }
    let mut total = 0u64;
    if let Ok(entries) = sftp.read_dir(remote_path).await {
        for entry in entries {
            let name = entry.file_name();
            if name == "." || name == ".." {
                continue;
            }
            let child = format!("{}/{}", remote_path.trim_end_matches('/'), name);
            if entry.file_type().is_dir() {
                total += Box::pin(remote_total_bytes(sftp, &child, true)).await;
            } else {
                total += entry.metadata().len();
            }
        }
    }
    total
}

/// Compute total bytes for a local path.
pub fn local_total_bytes(path: &Path) -> u64 {
    if path.is_file() {
        return path.metadata().map(|m| m.len()).unwrap_or(0);
    }
    let mut total = 0u64;
    if let Ok(entries) = std::fs::read_dir(path) {
        for entry in entries.flatten() {
            let child = entry.path();
            if child.is_dir() {
                total += local_total_bytes(&child);
            } else {
                total += child.metadata().map(|m| m.len()).unwrap_or(0);
            }
        }
    }
    total
}
