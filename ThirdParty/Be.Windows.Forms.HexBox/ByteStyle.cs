using System.Drawing;
using System;
using System.Collections.Generic;

namespace Be.Windows.Forms
{
	/// <summary>Defines foreground and background colors for one byte.</summary>
	public struct ByteStyle
	{
		/// <summary>Initializes a byte style.</summary>
		public ByteStyle(Color foreColor, Color backColor)
		{
			ForeColor = foreColor;
			BackColor = backColor;
		}

		/// <summary>Gets the foreground color.</summary>
		public Color ForeColor { get; private set; }
		/// <summary>Gets the background color.</summary>
		public Color BackColor { get; private set; }
	}

	/// <summary>Provides optional visual styles for byte offsets.</summary>
	public interface IByteStyleProvider
	{
		/// <summary>Gets the style for an offset when one is available.</summary>
		bool TryGetStyle(long index, out ByteStyle style);
	}

	internal sealed class ByteStyleBrushCache : IDisposable
	{
		readonly Dictionary<int, Brush> brushes = new Dictionary<int, Brush>();

		public Brush Get(Color color)
		{
			Brush brush;
			if (!brushes.TryGetValue(color.ToArgb(), out brush))
			{
				brush = new SolidBrush(color);
				brushes.Add(color.ToArgb(), brush);
			}
			return brush;
		}

		public void Dispose()
		{
			foreach (Brush brush in brushes.Values)
				brush.Dispose();
			brushes.Clear();
		}
	}
}
