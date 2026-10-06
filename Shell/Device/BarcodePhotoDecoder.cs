using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;
using SharpImage = SixLabors.ImageSharp.Image;

namespace BamboeUp.Mobile.Shell.Device;

public static class BarcodePhotoDecoder
{
    public static string? Decode(byte[] imageBytes)
    {
        using var image = SharpImage.Load<Rgba32>(imageBytes);
        var rgb = new byte[image.Width * image.Height * 3];
        var offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    rgb[offset++] = row[x].R;
                    rgb[offset++] = row[x].G;
                    rgb[offset++] = row[x].B;
                }
            }
        });
        var source = new RGBLuminanceSource(rgb, image.Width, image.Height);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats =
                [
                    BarcodeFormat.CODE_128,
                    BarcodeFormat.CODE_39,
                    BarcodeFormat.EAN_13,
                    BarcodeFormat.EAN_8,
                    BarcodeFormat.UPC_A,
                    BarcodeFormat.UPC_E,
                    BarcodeFormat.QR_CODE,
                    BarcodeFormat.ITF
                ]
            }
        };
        return reader.Decode(source)?.Text;
    }
}

public sealed class PhysicalInventoryDeviceProfile
{
    public static bool IsWifiConnected()
    {
        try
        {
            return Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi);
        }
        catch
        {
            return false;
        }
    }

    public static bool HasInternet()
    {
        try
        {
            return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        }
        catch
        {
            return false;
        }
    }
}
