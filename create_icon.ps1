Add-Type -AssemblyName System.Drawing

# Load the PNG image
$png = [System.Drawing.Image]::FromFile("assets\logo.png")

# Create different sizes for the ICO file
$sizes = @(16, 32, 48, 64, 128, 256)
$iconSizes = @()

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($png, $size, $size)
    $iconSizes += $bitmap
}

# For simplicity, let's just use the 256x256 version
$ico = New-Object System.Drawing.Bitmap($png, 256, 256)

# Save as ICO (this might not work perfectly, but let's try)
try {
    $ico.Save("LenovoLegionToolkit.WPF\Assets\icon_new.ico", [System.Drawing.Imaging.ImageFormat]::Icon)
    Write-Host "Icon created successfully"
} catch {
    Write-Host "Error creating icon: $($_.Exception.Message)"
}

# Clean up
$ico.Dispose()
$png.Dispose()
