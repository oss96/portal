#![windows_subsystem = "windows"]

mod app;
mod fs;
mod ssh;
mod transfer;

use anyhow::Result;
use clap::Parser;
use eframe::egui;
use std::path::PathBuf;

#[derive(Parser)]
#[command(name = "portal", about = "SSH file manager with GUI")]
struct Cli {
    /// Connection string in the format user@host (optional — shows dialog if omitted)
    connection: Option<String>,

    /// SSH port
    #[arg(short, long, default_value = "22")]
    port: u16,
}

fn main() -> Result<()> {
    let cli = Cli::parse();
    let runtime = tokio::runtime::Runtime::new()?;

    let icon = load_icon();
    let native_options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([1024.0, 640.0])
            .with_title("Portal")
            .with_icon(icon),
        ..Default::default()
    };

    // If connection string provided, try connecting before GUI launch
    let portal_app = if let Some(ref conn) = cli.connection {
        let (user, host) = conn
            .split_once('@')
            .ok_or_else(|| anyhow::anyhow!("Connection format: user@host"))?;

        if user.is_empty() || host.is_empty() {
            anyhow::bail!("Both user and host must be non-empty: user@host");
        }

        match runtime.block_on(ssh::connect(host, cli.port, user)) {
            Ok((handle, sftp)) => {
                app::PortalApp::connected(runtime, handle, sftp, user, host)?
            }
            Err(e) => {
                app::PortalApp::with_prefilled_connect(
                    runtime,
                    host,
                    user,
                    cli.port,
                    e.to_string(),
                )
            }
        }
    } else {
        app::PortalApp::with_connect_dialog(runtime)
    };

    eframe::run_native(
        "Portal",
        native_options,
        Box::new(move |cc| {
            setup_fonts(&cc.egui_ctx);
            Ok(Box::new(portal_app))
        }),
    )
    .map_err(|e| anyhow::anyhow!("{}", e))?;

    Ok(())
}

fn load_icon() -> egui::IconData {
    let png_bytes = include_bytes!("../assets/icon.png");
    let img = image::load_from_memory_with_format(png_bytes, image::ImageFormat::Png)
        .expect("failed to load icon")
        .into_rgba8();
    let (w, h) = img.dimensions();
    egui::IconData {
        rgba: img.into_raw(),
        width: w,
        height: h,
    }
}

fn setup_fonts(ctx: &egui::Context) {
    let windir = std::env::var("WINDIR").unwrap_or_else(|_| "C:\\Windows".into());
    let font_path = PathBuf::from(windir).join("Fonts").join("seguisym.ttf");
    if let Ok(font_data) = std::fs::read(&font_path) {
        let mut fonts = egui::FontDefinitions::default();
        fonts
            .font_data
            .insert("symbols".into(), egui::FontData::from_owned(font_data).into());
        // Add as fallback to the proportional font family
        if let Some(family) = fonts.families.get_mut(&egui::FontFamily::Proportional) {
            family.push("symbols".into());
        }
        ctx.set_fonts(fonts);
    }
}
