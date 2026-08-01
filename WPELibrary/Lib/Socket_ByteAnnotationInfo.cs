namespace WPELibrary.Lib
{
    public enum Socket_ByteAnnotationColor
    {
        Red, Orange, Yellow, Green, Blue, Purple
    }

    public class Socket_ByteAnnotationInfo
    {
        public int Start { get; set; }
        public int Length { get; set; }
        public string Note { get; set; }
        public Socket_ByteAnnotationColor Color { get; set; }
        public long End { get { return (long)Start + Length; } }
        public string RangeText
        {
            get
            {
                return Length <= 1
                    ? Start.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:X2}-{1:X2}", Start, End - 1);
            }
        }
        public Socket_ByteAnnotationInfo Clone()
        {
            return new Socket_ByteAnnotationInfo { Start = Start, Length = Length, Note = Note, Color = Color };
        }
    }
}
