using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // 同一会话中相同 UUID 共用皮肤请求，账号列表和主页不会各下载一遍。
    readonly Dictionary<string, Task<byte[]?>> skinRequests = new();

    UIElement AccountAvatar(Account? account, int size = 40)
    {
        var holder = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Background = Brush(lightTheme ? "#EEEAF7" : "#343044"),
            Child = new FontIcon { Glyph = "\uE77B", FontSize = size / 2 },
        };
        if (account?.Type == "microsoft" && Guid.TryParse(account.Uuid, out var id))
            _ = LoadAvatar(holder, id.ToString("N"));
        return holder;
    }

    // 优先使用一天内的缓存，网络失败时仍可回退到旧皮肤。
    async Task<byte[]?> FetchSkin(string uuid)
    {
        var cache = Path.Combine(store.Root, "skin-cache", uuid + ".png");
        try
        {
            if (
                File.Exists(cache)
                && new FileInfo(cache).Length <= 1024 * 1024
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(1)
            )
                return await File.ReadAllBytesAsync(cache);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var profile = await core.Json(
                "https://sessionserver.mojang.com/session/minecraft/profile/" + uuid,
                timeout.Token
            );
            var property = profile["properties"]
                .Items()
                .FirstOrDefault(p => p.S("name") == "textures");
            var textures = JsonNode.Parse(
                Encoding.UTF8.GetString(Convert.FromBase64String(property.S("value")))
            );
            if (
                !Uri.TryCreate(
                    textures?["textures"]?["SKIN"].S("url"),
                    UriKind.Absolute,
                    out var uri
                )
                || uri.Host != "textures.minecraft.net"
            )
                return null;
            var secure = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
            using var response = await core.Http.GetAsync(
                secure,
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1024 * 1024)
                return null;
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024)
                    return null;
                buffer.Write(bytes, 0, count);
            }
            var result = buffer.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            await File.WriteAllBytesAsync(cache, result, timeout.Token);
            return result;
        }
        catch
        {
            try
            {
                return File.Exists(cache) && new FileInfo(cache).Length <= 1024 * 1024
                    ? await File.ReadAllBytesAsync(cache)
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }

    async Task LoadAvatar(Border holder, string uuid)
    {
        try
        {
            if (!skinRequests.TryGetValue(uuid, out var request))
                skinRequests[uuid] = request = FetchSkin(uuid);
            var bytes = await request;
            if (bytes == null)
                return;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth != 64 || decoder.PixelHeight is not (32 or 64))
                return;
            var pixels = (
                await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage
                )
            ).DetachPixelData();
            // 截取皮肤上的 8×8 脸部和帽子层，放大时按整像素取样，保留像素边缘。
            var image = new byte[64 * 64 * 4];
            for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var face = ((8 + y / 8) * 64 + 8 + x / 8) * 4;
                var hat = ((8 + y / 8) * 64 + 40 + x / 8) * 4;
                var dest = (y * 64 + x) * 4;
                var alpha = pixels[hat + 3] / 255.0;
                for (var c = 0; c < 3; c++)
                    image[dest + c] = (byte)(
                        pixels[hat + c] * alpha + pixels[face + c] * (1 - alpha)
                    );
                image[dest + 3] = 255;
            }
            var bitmap = new WriteableBitmap(64, 64);
            using (var output = bitmap.PixelBuffer.AsStream())
                await output.WriteAsync(image);
            bitmap.Invalidate();
            holder.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        }
        catch
        { /* An unavailable skin never prevents account selection or launching. */
        }
    }
}
