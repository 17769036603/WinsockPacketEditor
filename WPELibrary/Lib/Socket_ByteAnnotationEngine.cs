using Be.Windows.Forms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml.Linq;

namespace WPELibrary.Lib
{
    public static class Socket_ByteAnnotationEngine
    {
        public static List<Socket_ByteAnnotationInfo> Clone(IEnumerable<Socket_ByteAnnotationInfo> source)
        {
            return source == null ? new List<Socket_ByteAnnotationInfo>() : source.Select(x => x.Clone()).ToList();
        }

        public static bool Overlaps(IEnumerable<Socket_ByteAnnotationInfo> source, int start, int length, Socket_ByteAnnotationInfo ignored)
        {
            long end = (long)start + length;
            return source != null && source.Any(x => !ReferenceEquals(x, ignored) && start < x.End && x.Start < end);
        }

        public static void AdjustForInsert(IList<Socket_ByteAnnotationInfo> source, long index, long length)
        {
            if (source == null || length <= 0) return;
            foreach (Socket_ByteAnnotationInfo item in source)
            {
                if (index < item.Start) item.Start += checked((int)length);
                else if (index <= item.End) item.Length += checked((int)length);
            }
        }

        public static void AdjustForDelete(IList<Socket_ByteAnnotationInfo> source, long index, long length)
        {
            if (source == null || length <= 0) return;
            long deleteEnd = index + length;
            for (int i = source.Count - 1; i >= 0; i--)
            {
                Socket_ByteAnnotationInfo item = source[i];
                long newStart = Map(item.Start, index, deleteEnd, length);
                long newEnd = Map(item.End, index, deleteEnd, length);
                if (newEnd <= newStart) source.RemoveAt(i);
                else { item.Start = checked((int)newStart); item.Length = checked((int)(newEnd - newStart)); }
            }
        }

        private static long Map(long position, long start, long end, long length)
        {
            if (position <= start) return position;
            if (position < end) return start;
            return position - length;
        }

        public static string Serialize(IEnumerable<Socket_ByteAnnotationInfo> source)
        {
            XElement root = ToXElement(source);
            return root.HasElements ? root.ToString(SaveOptions.DisableFormatting) : string.Empty;
        }

        public static List<Socket_ByteAnnotationInfo> Deserialize(string value)
        {
            return Deserialize(value, int.MaxValue);
        }

        public static List<Socket_ByteAnnotationInfo> Deserialize(string value, int bufferLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<Socket_ByteAnnotationInfo>();
            try { return FromXElement(XElement.Parse(value), bufferLength); }
            catch (System.Xml.XmlException) { return new List<Socket_ByteAnnotationInfo>(); }
        }

        public static XElement ToXElement(IEnumerable<Socket_ByteAnnotationInfo> source)
        {
            return new XElement("Annotations", Normalize(Clone(source), int.MaxValue).Select(x => new XElement("Annotation",
                new XAttribute("Start", x.Start), new XAttribute("Length", x.Length),
                new XAttribute("Color", x.Color), new XCData(x.Note ?? string.Empty))));
        }

        public static List<Socket_ByteAnnotationInfo> FromXElement(XElement root)
        {
            return FromXElement(root, int.MaxValue);
        }

        public static List<Socket_ByteAnnotationInfo> FromXElement(XElement root, int bufferLength)
        {
            List<Socket_ByteAnnotationInfo> result = new List<Socket_ByteAnnotationInfo>();
            if (root == null) return result;
            foreach (XElement node in root.Elements("Annotation"))
            {
                int start, length;
                Socket_ByteAnnotationColor color;
                if (!int.TryParse((string)node.Attribute("Start"), out start) ||
                    !int.TryParse((string)node.Attribute("Length"), out length) || start < 0 || length <= 0) continue;
                if (!Enum.TryParse((string)node.Attribute("Color"), true, out color) ||
                    !Enum.IsDefined(typeof(Socket_ByteAnnotationColor), color))
                    color = Socket_ByteAnnotationColor.Yellow;
                result.Add(new Socket_ByteAnnotationInfo { Start = start, Length = length, Color = color, Note = node.Value });
            }
            return Normalize(result, bufferLength);
        }

        private static List<Socket_ByteAnnotationInfo> Normalize(
            IEnumerable<Socket_ByteAnnotationInfo> source, int bufferLength)
        {
            List<Socket_ByteAnnotationInfo> normalized = new List<Socket_ByteAnnotationInfo>();
            long previousEnd = -1;
            foreach (Socket_ByteAnnotationInfo item in source.OrderBy(x => x.Start))
            {
                if (item.Start < 0 || item.Length <= 0 ||
                    item.End > bufferLength || item.Start < previousEnd)
                    continue;
                normalized.Add(item);
                previousEnd = item.End;
            }
            return normalized;
        }

        public static Color GetBackColor(Socket_ByteAnnotationColor color)
        {
            switch (color)
            {
                case Socket_ByteAnnotationColor.Red: return Color.FromArgb(255, 214, 214);
                case Socket_ByteAnnotationColor.Orange: return Color.FromArgb(255, 228, 194);
                case Socket_ByteAnnotationColor.Green: return Color.FromArgb(215, 242, 208);
                case Socket_ByteAnnotationColor.Blue: return Color.FromArgb(214, 233, 255);
                case Socket_ByteAnnotationColor.Purple: return Color.FromArgb(231, 217, 255);
                default: return Color.FromArgb(255, 243, 176);
            }
        }

        public static byte[] GetBytes(IByteProvider provider)
        {
            Socket_AnnotatedByteProvider annotated = provider as Socket_AnnotatedByteProvider;
            if (annotated != null) return annotated.Bytes.ToArray();
            DynamicByteProvider dynamic = provider as DynamicByteProvider;
            return dynamic == null ? new byte[0] : dynamic.Bytes.ToArray();
        }

        public static List<Socket_ByteAnnotationInfo> ForSelection(
            IEnumerable<Socket_ByteAnnotationInfo> source, long selectionStart, long selectionLength)
        {
            if (selectionLength <= 0) return Clone(source);
            long selectionEnd = checked(selectionStart + selectionLength);
            return (source ?? Enumerable.Empty<Socket_ByteAnnotationInfo>())
                .Select(x => new
                {
                    Annotation = x,
                    Start = Math.Max((long)x.Start, selectionStart),
                    End = Math.Min(x.End, selectionEnd)
                })
                .Where(x => x.End > x.Start)
                .Select(x => new Socket_ByteAnnotationInfo
                {
                    Start = checked((int)(x.Start - selectionStart)),
                    Length = checked((int)(x.End - x.Start)),
                    Note = x.Annotation.Note,
                    Color = x.Annotation.Color
                }).ToList();
        }
    }

    public sealed class Socket_ByteAnnotationStyleProvider : IByteStyleProvider
    {
        private readonly IList<Socket_ByteAnnotationInfo> annotations;
        public Socket_ByteAnnotationStyleProvider(IList<Socket_ByteAnnotationInfo> annotations) { this.annotations = annotations; }
        public bool TryGetStyle(long index, out ByteStyle style)
        {
            Socket_ByteAnnotationInfo item = annotations == null ? null : annotations.FirstOrDefault(x => index >= x.Start && index < x.End);
            if (item == null) { style = new ByteStyle(); return false; }
            style = new ByteStyle(Color.FromArgb(31, 31, 31), Socket_ByteAnnotationEngine.GetBackColor(item.Color));
            return true;
        }
    }

    public sealed class Socket_AnnotatedByteProvider : IByteProvider
    {
        private readonly DynamicByteProvider inner;
        private readonly IList<Socket_ByteAnnotationInfo> annotations;
        private readonly IList<PresetVariableBinding> variableBindings;
        public Socket_AnnotatedByteProvider(byte[] bytes, IList<Socket_ByteAnnotationInfo> annotations)
            : this(bytes, annotations, null)
        {
        }

        public Socket_AnnotatedByteProvider(
            byte[] bytes,
            IList<Socket_ByteAnnotationInfo> annotations,
            IList<PresetVariableBinding> variableBindings)
        {
            inner = new DynamicByteProvider(bytes ?? new byte[0]);
            this.annotations = annotations;
            this.variableBindings = variableBindings;
        }
        public IList<byte> Bytes { get { return inner.Bytes; } }
        public byte ReadByte(long index) { return inner.ReadByte(index); }
        public void WriteByte(long index, byte value)
        {
            if (index < 0 || index >= Length) throw new ArgumentOutOfRangeException("index");
            inner.WriteByte(index, value);
            OnChanged();
        }
        public void InsertBytes(long index, byte[] bs)
        {
            if (bs == null) throw new ArgumentNullException("bs");
            if (index < 0 || index > Length) throw new ArgumentOutOfRangeException("index");
            if (variableBindings != null && variableBindings.Any(item =>
                item != null && index > item.Offset && index < item.End))
            {
                throw new InvalidOperationException("变量绑定范围内不能插入字节，请先取消变量绑定。");
            }
            inner.InsertBytes(index, bs);
            Socket_ByteAnnotationEngine.AdjustForInsert(annotations, index, bs.Length);
            if (variableBindings != null)
            {
                foreach (PresetVariableBinding item in variableBindings)
                {
                    if (item != null && index <= item.Offset)
                    {
                        item.Offset = checked(item.Offset + bs.Length);
                    }
                }
            }
            OnLengthChanged();
            OnChanged();
        }
        public void DeleteBytes(long index, long length)
        {
            if (index < 0 || index > Length) throw new ArgumentOutOfRangeException("index");
            if (length < 0 || length > Length - index) throw new ArgumentOutOfRangeException("length");
            if (variableBindings != null && variableBindings.Any(item =>
                item != null && DynamicVariableRange.Overlaps(item.Offset, item.Length, (int)index, (int)length)))
            {
                throw new InvalidOperationException("变量绑定范围内不能删除字节，请先取消变量绑定。");
            }
            inner.DeleteBytes(index, length);
            Socket_ByteAnnotationEngine.AdjustForDelete(annotations, index, length);
            if (variableBindings != null && length > 0)
            {
                foreach (PresetVariableBinding item in variableBindings)
                {
                    if (item != null && index < item.Offset)
                    {
                        item.Offset = checked(item.Offset - (int)Math.Min(length, item.Offset - index));
                    }
                }
            }
            OnLengthChanged();
            OnChanged();
        }
        public long Length { get { return inner.Length; } }
        public event EventHandler LengthChanged;
        public event EventHandler Changed;
        private void OnLengthChanged() { if (LengthChanged != null) LengthChanged(this, EventArgs.Empty); }
        private void OnChanged() { if (Changed != null) Changed(this, EventArgs.Empty); }
        public bool HasChanges() { return inner.HasChanges(); }
        public void ApplyChanges() { inner.ApplyChanges(); }
        public bool SupportsWriteByte() { return true; }
        public bool SupportsInsertBytes() { return true; }
        public bool SupportsDeleteBytes() { return true; }
    }
}
