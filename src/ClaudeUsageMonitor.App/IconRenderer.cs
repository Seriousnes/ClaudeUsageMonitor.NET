using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.App;

/// <summary>Renders a flat colored-circle tray icon for a Status. Caller owns the returned Icon and must dispose the previous one.</summary>
public static class IconRenderer
{
    private const int Size = 32;

    public static Icon Render(Status status)
    {
        var rgb = StatusPalette.Rgb(status);
        var color = Color.FromArgb(rgb.R, rgb.G, rgb.B);

        using var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 4, 4, 24, 24);
        }

        // Build a 32bpp BGRA .ico in memory and let Icon own the managed bytes — no GetHicon/HICON,
        // so no native handle to free. A DIB (BMP) payload is used rather than PNG: GDI's Icon(Stream)
        // decodes PNG-compressed icon entries unreliably, whereas the DIB form is universally supported.
        using var ms = new MemoryStream();
        WriteIco(ms, bmp);
        ms.Position = 0;
        return new Icon(ms);
    }

    private static void WriteIco(Stream stream, Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int xorSize = w * h * 4;                       // 32bpp BGRA color data
        int andStride = ((w + 31) / 32) * 4;           // 1bpp mask, 4-byte aligned rows
        int andSize = andStride * h;
        int dibSize = 40 + xorSize + andSize;          // BITMAPINFOHEADER + color + mask

        using var bw = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // ICONDIR
        bw.Write((short)0);                            // reserved
        bw.Write((short)1);                            // type = icon
        bw.Write((short)1);                            // image count

        // ICONDIRENTRY
        bw.Write((byte)w);
        bw.Write((byte)h);
        bw.Write((byte)0);                             // palette color count (0 = none)
        bw.Write((byte)0);                             // reserved
        bw.Write((short)1);                            // color planes
        bw.Write((short)32);                           // bits per pixel
        bw.Write(dibSize);                             // bytes of DIB that follow
        bw.Write(22);                                  // offset of DIB (6 + 16)

        // BITMAPINFOHEADER
        bw.Write(40);                                  // header size
        bw.Write(w);
        bw.Write(h * 2);                               // height counts color + AND mask
        bw.Write((short)1);                            // planes
        bw.Write((short)32);                           // bpp
        bw.Write(0);                                   // BI_RGB
        bw.Write(xorSize);
        bw.Write(0); bw.Write(0);                      // pixels-per-metre x/y
        bw.Write(0); bw.Write(0);                      // colors used / important

        // XOR color data, stored bottom-up
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = h - 1; y >= 0; y--)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                bw.Write(row);
            }
        }
        finally { bmp.UnlockBits(data); }

        // AND mask: all zero — per-pixel alpha in the color data handles transparency
        bw.Write(new byte[andSize]);
        bw.Flush();
    }
}
