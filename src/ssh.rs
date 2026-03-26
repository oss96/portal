use anyhow::{bail, Context, Result};
use russh::client;
use russh::keys::{self, PrivateKeyWithHashAlg};
use russh_sftp::client::SftpSession;
use std::path::PathBuf;
use std::sync::Arc;

pub struct Handler;

impl client::Handler for Handler {
    type Error = anyhow::Error;

    async fn check_server_key(
        &mut self,
        _server_public_key: &keys::PublicKey,
    ) -> Result<bool, Self::Error> {
        // Accept all server keys (TODO: verify against known_hosts)
        Ok(true)
    }
}

/// Connect to an SSH host and return the session handle + SFTP session.
/// The handle must be kept alive for the SFTP session to work.
pub async fn connect(
    host: &str,
    port: u16,
    user: &str,
) -> Result<(client::Handle<Handler>, SftpSession)> {
    let home = dirs::home_dir().context("Cannot determine home directory")?;
    let ssh_dir = home.join(".ssh");

    // Try to load SSH config if it exists
    let config_path = ssh_dir.join("config");
    let (resolved_host, resolved_port, resolved_user) = if config_path.exists() {
        resolve_ssh_config(&config_path, host, port, user)?
    } else {
        (host.to_string(), port, user.to_string())
    };

    // Try loading SSH keys in order of preference
    let key = load_ssh_key(&ssh_dir)?;
    let key_with_hash = PrivateKeyWithHashAlg::new(Arc::new(key), None);

    let config = Arc::new(client::Config {
        window_size: 32 * 1024 * 1024,    // 32 MB window (default 2 MB)
        maximum_packet_size: 65535,         // 64 KB packets (default 32 KB)
        ..Default::default()
    });
    let mut handle = client::connect(config, (&*resolved_host, resolved_port), Handler)
        .await
        .context("Failed to connect to SSH host")?;

    let auth_result = handle
        .authenticate_publickey(&resolved_user, key_with_hash)
        .await
        .context("SSH authentication failed")?;

    if !matches!(auth_result, client::AuthResult::Success) {
        bail!("SSH authentication rejected by server");
    }

    // Open SFTP subsystem
    let channel = handle.channel_open_session().await?;
    channel.request_subsystem(true, "sftp").await?;
    let sftp = SftpSession::new(channel.into_stream()).await?;

    Ok((handle, sftp))
}

fn load_ssh_key(ssh_dir: &PathBuf) -> Result<keys::PrivateKey> {
    let key_names = ["id_ed25519", "id_rsa", "id_ecdsa"];

    for name in &key_names {
        let path = ssh_dir.join(name);
        if path.exists() {
            match keys::load_secret_key(&path, None) {
                Ok(key) => return Ok(key),
                Err(_) => continue,
            }
        }
    }

    bail!(
        "No usable SSH key found in {}. Tried: {}",
        ssh_dir.display(),
        key_names.join(", ")
    )
}

fn resolve_ssh_config(
    config_path: &PathBuf,
    host: &str,
    port: u16,
    user: &str,
) -> Result<(String, u16, String)> {
    use ssh2_config::{ParseRule, SshConfig};
    use std::io::BufReader;

    let file = std::fs::File::open(config_path)?;
    let mut reader = BufReader::new(file);
    let config = SshConfig::default().parse(&mut reader, ParseRule::ALLOW_UNKNOWN_FIELDS)?;
    let params = config.query(host);

    let resolved_host = params
        .host_name
        .as_deref()
        .unwrap_or(host)
        .to_string();
    let resolved_port = params.port.unwrap_or(port);
    let resolved_user = params
        .user
        .as_deref()
        .unwrap_or(user)
        .to_string();

    Ok((resolved_host, resolved_port, resolved_user))
}
