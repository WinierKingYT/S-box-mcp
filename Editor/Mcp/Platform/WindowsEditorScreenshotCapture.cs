using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Editor;

internal sealed class WindowsEditorScreenshotCapture : IEditorScreenshotCapture
{
	private const int SourceCopy = 0x00CC0020;

	public string Capture( string destinationPath )
	{
		var window = GetEditorWindowHandle();
		if ( window == IntPtr.Zero )
			return "Error: No main window found for editor process";
		if ( !GetWindowRect( window, out var rectangle ) )
			return "Error: Failed to get window rect";

		var width = rectangle.Right - rectangle.Left;
		var height = rectangle.Bottom - rectangle.Top;
		if ( width <= 0 || height <= 0 )
			return "Error: Invalid window bounds";

		var sourceDc = GetWindowDC( window );
		if ( sourceDc == IntPtr.Zero )
			return "Error: Failed to access editor window";

		var destinationDc = CreateCompatibleDC( sourceDc );
		var bitmapHandle = CreateCompatibleBitmap( sourceDc, width, height );
		if ( destinationDc == IntPtr.Zero || bitmapHandle == IntPtr.Zero )
		{
			if ( bitmapHandle != IntPtr.Zero ) DeleteObject( bitmapHandle );
			if ( destinationDc != IntPtr.Zero ) DeleteDC( destinationDc );
			ReleaseDC( window, sourceDc );
			return "Error: Failed to create screenshot bitmap";
		}

		var oldObject = SelectObject( destinationDc, bitmapHandle );
		BitBlt(
			destinationDc,
			0,
			0,
			width,
			height,
			sourceDc,
			0,
			0,
			SourceCopy );
		SelectObject( destinationDc, oldObject );

		try
		{
			var imageInfo = new SKImageInfo(
				width,
				height,
				SKColorType.Bgra8888,
				SKAlphaType.Premul );
			using var bitmap = new SKBitmap( imageInfo );
			var bitmapInfo = new BitmapInfo
			{
				Header =
				{
					Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
					Width = width,
					Height = -height,
					Planes = 1,
					BitCount = 32,
					Compression = 0
				}
			};

			var copiedLines = GetDIBits(
				sourceDc,
				bitmapHandle,
				0,
				(uint)height,
				bitmap.GetPixels(),
				ref bitmapInfo,
				0 );
			if ( copiedLines == 0 )
				return "Error: Failed to read screenshot pixels";

			using var image = SKImage.FromBitmap( bitmap );
			using var data = image.Encode( SKEncodedImageFormat.Png, 100 );
			using var stream = File.Create( destinationPath );
			data.SaveTo( stream );
			return "Success: Screenshot saved to " + destinationPath;
		}
		catch ( Exception exception )
		{
			return "Error: " + exception.Message;
		}
		finally
		{
			DeleteObject( bitmapHandle );
			DeleteDC( destinationDc );
			ReleaseDC( window, sourceDc );
		}
	}

	private static IntPtr GetEditorWindowHandle()
	{
		var processId = (uint)Process.GetCurrentProcess().Id;
		var targetWindow = IntPtr.Zero;
		var largestArea = 0;

		EnumWindows( (window, _) =>
		{
			GetWindowThreadProcessId( window, out var windowProcessId );
			if ( windowProcessId != processId ) return true;

			var titleBuffer = new StringBuilder( 256 );
			GetWindowText( window, titleBuffer, titleBuffer.Capacity );
			if ( titleBuffer.Length == 0 || !GetWindowRect( window, out var rectangle ) )
				return true;

			var width = rectangle.Right - rectangle.Left;
			var height = rectangle.Bottom - rectangle.Top;
			var area = width * height;
			if ( width <= 300 || height <= 300 || area <= largestArea ) return true;

			targetWindow = window;
			largestArea = area;
			return true;
		}, IntPtr.Zero );

		return targetWindow;
	}

	[DllImport( "user32.dll" )]
	private static extern IntPtr GetWindowDC( IntPtr window );

	[DllImport( "user32.dll" )]
	private static extern int ReleaseDC( IntPtr window, IntPtr deviceContext );

	[DllImport( "gdi32.dll" )]
	private static extern IntPtr CreateCompatibleDC( IntPtr deviceContext );

	[DllImport( "gdi32.dll" )]
	private static extern IntPtr CreateCompatibleBitmap(
		IntPtr deviceContext,
		int width,
		int height );

	[DllImport( "gdi32.dll" )]
	private static extern IntPtr SelectObject(
		IntPtr deviceContext,
		IntPtr graphicsObject );

	[DllImport( "gdi32.dll" )]
	private static extern bool DeleteDC( IntPtr deviceContext );

	[DllImport( "gdi32.dll" )]
	private static extern bool DeleteObject( IntPtr graphicsObject );

	[DllImport( "gdi32.dll" )]
	private static extern bool BitBlt(
		IntPtr destination,
		int destinationX,
		int destinationY,
		int width,
		int height,
		IntPtr source,
		int sourceX,
		int sourceY,
		int operation );

	[DllImport( "gdi32.dll" )]
	private static extern int GetDIBits(
		IntPtr deviceContext,
		IntPtr bitmap,
		uint startScan,
		uint scanLines,
		IntPtr pixels,
		ref BitmapInfo bitmapInfo,
		uint usage );

	[DllImport( "user32.dll" )]
	private static extern bool GetWindowRect(
		IntPtr window,
		out WindowRectangle rectangle );

	[DllImport( "user32.dll" )]
	private static extern bool EnumWindows(
		EnumWindowsCallback callback,
		IntPtr state );

	[DllImport( "user32.dll", CharSet = CharSet.Auto, SetLastError = true )]
	private static extern int GetWindowText(
		IntPtr window,
		StringBuilder text,
		int maxCount );

	[DllImport( "user32.dll" )]
	private static extern uint GetWindowThreadProcessId(
		IntPtr window,
		out uint processId );

	private delegate bool EnumWindowsCallback( IntPtr window, IntPtr state );

	[StructLayout( LayoutKind.Sequential )]
	private struct WindowRectangle
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BitmapInfoHeader
	{
		public uint Size;
		public int Width;
		public int Height;
		public ushort Planes;
		public ushort BitCount;
		public uint Compression;
		public uint SizeImage;
		public int XPelsPerMeter;
		public int YPelsPerMeter;
		public uint ColorsUsed;
		public uint ColorsImportant;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BitmapInfo
	{
		public BitmapInfoHeader Header;
		public int Colors;
	}
}
