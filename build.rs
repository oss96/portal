fn main() {
    #[cfg(windows)]
    {
        // Generate .ico from PNG (ICO header wrapping PNG data)
        let png = std::fs::read("assets/icon.png").expect("assets/icon.png not found");
        let png_len = png.len() as u32;

        let mut ico = Vec::with_capacity(22 + png.len());
        // ICO header: reserved=0, type=1 (icon), count=1
        ico.extend_from_slice(&0u16.to_le_bytes()); // reserved
        ico.extend_from_slice(&1u16.to_le_bytes()); // type: icon
        ico.extend_from_slice(&1u16.to_le_bytes()); // image count
        // Directory entry: 256x256 (0=256), 0 colors, 0 reserved, 1 plane, 32bpp, size, offset=22
        ico.push(0); // width (0 = 256)
        ico.push(0); // height (0 = 256)
        ico.push(0); // color palette count
        ico.push(0); // reserved
        ico.extend_from_slice(&1u16.to_le_bytes()); // color planes
        ico.extend_from_slice(&32u16.to_le_bytes()); // bits per pixel
        ico.extend_from_slice(&png_len.to_le_bytes()); // image size
        ico.extend_from_slice(&22u32.to_le_bytes()); // offset to image data
        ico.extend_from_slice(&png);

        let ico_path = format!("{}/icon.ico", std::env::var("OUT_DIR").unwrap());
        std::fs::write(&ico_path, ico).expect("failed to write icon.ico");

        let mut res = winresource::WindowsResource::new();
        res.set_icon(&ico_path);
        res.compile().expect("failed to compile Windows resources");
    }
}
