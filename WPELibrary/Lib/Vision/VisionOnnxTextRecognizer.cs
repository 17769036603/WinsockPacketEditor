using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json.Linq;

namespace WPELibrary.Lib.Vision
{
    public sealed class VisionOnnxTextRecognizer : IVisionTextRecognizer, IDisposable
    {
        private readonly object modelSync = new object();
        private VisionOnnxModelSet modelSet;
        private string modelDirectory;
        private string modelSignature;

        public VisionOcrResult Recognize(
            Bitmap source,
            VisionOcrOptions options,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            options.Validate();
            if (cancellationToken.IsCancellationRequested)
            {
                return VisionOcrResult.CancelledResult();
            }

            lock (this.modelSync)
            {
                string directory = ResolveModelDirectory(options.OnnxModelDirectory);
                VisionOnnxModelSet current;
                string loadError;
                if (!TryGetModelSet(directory, out current, out loadError))
                {
                    return VisionOcrResult.Unavailable(loadError);
                }

                try
                {
                    VisionImageInputSpec detectionInput = ResolveInputSpec(
                        current.DetectionSession,
                        current.DetectionInputName,
                        source.Size,
                        options.OnnxMaxImageSide,
                        960,
                        960);
                    VisionTensorData detectionOutput = RunImageModel(
                        current.DetectionSession,
                        current.DetectionInputName,
                        CreateImageTensor(source, detectionInput, current.DetectionPreprocess),
                        cancellationToken);
                    if (detectionOutput == null)
                    {
                        return VisionOcrResult.CancelledResult();
                    }

                    IList<Rectangle> boxes = ExtractDetectionBoxes(
                        detectionOutput,
                        detectionInput.Width,
                        detectionInput.Height,
                        source.Size,
                        options.OnnxDetectionThreshold,
                        current.DetectionPreprocess.OutputIsLogits,
                        cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return VisionOcrResult.CancelledResult();
                    }
                    if (boxes.Count == 0)
                    {
                        return VisionOcrResult.Failed(
                            "ONNX OCR returned no text boxes.",
                            string.Empty,
                            0D,
                            0);
                    }

                    VisionImageInputSpec recognitionInput = ResolveInputSpec(
                        current.RecognitionSession,
                        current.RecognitionInputName,
                        new Size(320, 48),
                        options.OnnxMaxImageSide,
                        48,
                        320);
                    StringBuilder text = new StringBuilder();
                    List<VisionOcrTextBox> textBoxes = new List<VisionOcrTextBox>();
                    List<double> confidences = new List<double>();
                    foreach (Rectangle box in boxes)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return VisionOcrResult.CancelledResult();
                        }

                        using (Bitmap crop = Crop(source, box))
                        {
                            VisionTensorData recognitionOutput = RunImageModel(
                                current.RecognitionSession,
                                current.RecognitionInputName,
                                CreateImageTensor(crop, recognitionInput, current.RecognitionPreprocess),
                                cancellationToken);
                            if (recognitionOutput == null)
                            {
                                return VisionOcrResult.CancelledResult();
                            }

                            VisionRecognitionData decoded = DecodeRecognition(
                                recognitionOutput,
                                current.Keys,
                                current.RecognitionPreprocess.OutputIsLogits);
                            if (string.IsNullOrEmpty(decoded.Text))
                            {
                                continue;
                            }

                            if (text.Length > 0)
                            {
                                text.Append(' ');
                            }
                            text.Append(decoded.Text);
                            textBoxes.Add(new VisionOcrTextBox(
                                decoded.Text,
                                box,
                                decoded.Confidence));
                            confidences.Add(decoded.Confidence);
                        }
                    }

                    if (text.Length == 0)
                    {
                        return VisionOcrResult.Failed(
                            "ONNX OCR returned no text.",
                            string.Empty,
                            0D,
                            0);
                    }

                    double confidence = confidences.Count == 0
                        ? 0D
                        : confidences.Sum() / confidences.Count;
                    return VisionOcrResult.Succeeded(text.ToString(), confidence, textBoxes);
                }
                catch (OperationCanceledException)
                {
                    return VisionOcrResult.CancelledResult();
                }
                catch (Exception ex)
                {
                    return VisionOcrResult.Failed(
                        "ONNX OCR inference failed: " + ex.Message,
                        string.Empty,
                        0D,
                        -1);
                }
            }
        }

        public static string DescribeModelDirectory(string configuredDirectory)
        {
            string directory = ResolveModelDirectory(configuredDirectory);
            if (!Directory.Exists(directory))
            {
                return "ONNX model directory was not found: " + directory;
            }

            string detection = FindModel(directory, "det.onnx", "dbnet.onnx");
            string recognition = FindModel(directory, "rec.onnx", "crnn_lite_lstm.onnx");
            string keys = FindModel(directory, "keys.txt", "character_dict.txt");
            if (detection == null || recognition == null || keys == null)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "ONNX model set incomplete (det={0}, rec={1}, keys={2}).",
                    detection == null ? "missing" : "ok",
                    recognition == null ? "missing" : "ok",
                    keys == null ? "missing" : "ok");
            }
            return "ONNX model set ready: " + directory;
        }

        public void Dispose()
        {
            lock (this.modelSync)
            {
                if (this.modelSet != null)
                {
                    this.modelSet.Dispose();
                    this.modelSet = null;
                    this.modelDirectory = null;
                    this.modelSignature = null;
                }
            }
        }

        private bool TryGetModelSet(
            string directory,
            out VisionOnnxModelSet current,
            out string error)
        {
            lock (this.modelSync)
            {
                if (!Directory.Exists(directory))
                {
                    current = null;
                    error = "ONNX model directory was not found: " + directory;
                    return false;
                }

                string signature = BuildModelSignature(directory);
                if (this.modelSet != null &&
                    string.Equals(this.modelDirectory, directory, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(this.modelSignature, signature, StringComparison.Ordinal))
                {
                    current = this.modelSet;
                    error = string.Empty;
                    return true;
                }

                string detectionPath = FindModel(directory, "det.onnx", "dbnet.onnx");
                string recognitionPath = FindModel(directory, "rec.onnx", "crnn_lite_lstm.onnx");
                string keysPath = FindModel(directory, "keys.txt", "character_dict.txt");
                if (detectionPath == null || recognitionPath == null || keysPath == null)
                {
                    current = null;
                    error = DescribeModelDirectory(directory);
                    return false;
                }

                VisionOnnxModelSet loaded = null;
                try
                {
                    loaded = new VisionOnnxModelSet(
                        detectionPath,
                        recognitionPath,
                        ReadKeys(keysPath));
                    if (loaded.Keys.Count == 0)
                    {
                        throw new InvalidDataException("The ONNX OCR keys file is empty.");
                    }

                    if (this.modelSet != null)
                    {
                        this.modelSet.Dispose();
                    }
                    this.modelSet = loaded;
                    this.modelDirectory = directory;
                    this.modelSignature = signature;
                    current = loaded;
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex)
                {
                    if (loaded != null)
                    {
                        loaded.Dispose();
                    }
                    current = null;
                    error = "ONNX OCR model load failed: " + ex.Message;
                    return false;
                }
            }
        }

        private static VisionTensorData RunImageModel(
            InferenceSession session,
            string inputName,
            DenseTensor<float> input,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, input)
            };
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(inputs))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
                if (outputs == null || outputs.Count == 0)
                {
                    throw new InvalidDataException("The ONNX model returned no outputs.");
                }

                foreach (DisposableNamedOnnxValue output in outputs)
                {
                    try
                    {
                        Tensor<float> tensor = output.AsTensor<float>();
                        int[] dimensions = tensor.Dimensions.ToArray();
                        int length = checked((int)tensor.Length);
                        float[] values = new float[length];
                        for (int index = 0; index < length; index++)
                        {
                            values[index] = tensor.GetValue(index);
                        }
                        return new VisionTensorData(dimensions, values);
                    }
                    catch (InvalidCastException)
                    {
                        // Some models expose auxiliary non-float outputs before the score map.
                    }
                    catch (InvalidOperationException)
                    {
                        // Keep looking for the first usable float tensor.
                    }
                }
                throw new InvalidDataException("The ONNX model did not return a float tensor output.");
            }
        }

        private static DenseTensor<float> CreateImageTensor(
            Bitmap source,
            VisionImageInputSpec inputSpec,
            VisionImagePreprocessProfile preprocess)
        {
            int width = inputSpec.Width;
            int height = inputSpec.Height;
            int channels = inputSpec.Channels;
            using (Bitmap resized = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(resized))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));

                Rectangle bounds = new Rectangle(0, 0, width, height);
                BitmapData data = resized.LockBits(
                    bounds,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] pixels = new byte[stride * height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    int planeSize = width * height;
                    float[] values = new float[channels * planeSize];
                    for (int y = 0; y < height; y++)
                    {
                        int rowOffset = data.Stride >= 0
                            ? y * stride
                            : (height - 1 - y) * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int pixelOffset = rowOffset + x * 4;
                            float blue = pixels[pixelOffset] / 255F;
                            float green = pixels[pixelOffset + 1] / 255F;
                            float red = pixels[pixelOffset + 2] / 255F;
                            float alpha = pixels[pixelOffset + 3] / 255F;
                            int offset = y * width + x;
                            if (channels == 1)
                            {
                                WriteTensorValue(
                                    values,
                                    inputSpec,
                                    preprocess,
                                    offset,
                                    0,
                                    red,
                                    green,
                                    blue,
                                    alpha);
                            }
                            else
                            {
                                WriteTensorValue(values, inputSpec, preprocess, offset, 0, red, green, blue, alpha);
                                WriteTensorValue(values, inputSpec, preprocess, offset, 1, red, green, blue, alpha);
                                WriteTensorValue(values, inputSpec, preprocess, offset, 2, red, green, blue, alpha);
                                if (channels == 4)
                                {
                                    WriteTensorValue(values, inputSpec, preprocess, offset, 3, red, green, blue, alpha);
                                }
                            }
                        }
                    }
                    return new DenseTensor<float>(
                        values,
                        inputSpec.ChannelsLast
                            ? new[] { 1, height, width, channels }
                            : new[] { 1, channels, height, width });
                }
                finally
                {
                    resized.UnlockBits(data);
                }
            }
        }

        private static IList<Rectangle> ExtractDetectionBoxes(
            VisionTensorData output,
            int inputWidth,
            int inputHeight,
            Size sourceSize,
            double threshold,
            bool outputIsLogits,
            CancellationToken cancellationToken)
        {
            List<Rectangle> directBoxes = TryExtractDirectBoxes(
                output,
                inputWidth,
                inputHeight,
                sourceSize);
            if (directBoxes.Count > 0)
            {
                return directBoxes;
            }

            int[] dimensions = output.Dimensions ?? new int[0];
            if (dimensions.Length < 2)
            {
                return new List<Rectangle>();
            }
            int mapHeight = dimensions[dimensions.Length - 2];
            int mapWidth = dimensions[dimensions.Length - 1];
            if (mapHeight <= 0 || mapWidth <= 0 || output.Values.Length < mapHeight * mapWidth)
            {
                return new List<Rectangle>();
            }

            int mapLength = checked(mapHeight * mapWidth);
            int valueOffset = output.Values.Length - mapLength;
            bool[] visited = new bool[mapLength];
            List<Rectangle> boxes = new List<Rectangle>();
            for (int y = 0; y < mapHeight; y++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return boxes;
                }
                for (int x = 0; x < mapWidth; x++)
                {
                    int index = y * mapWidth + x;
                    if (visited[index] ||
                        Probability(output.Values[valueOffset + index], outputIsLogits) < threshold)
                    {
                        continue;
                    }

                    int minX = x;
                    int minY = y;
                    int maxX = x;
                    int maxY = y;
                    int componentSize = 0;
                    Queue<int> queue = new Queue<int>();
                    queue.Enqueue(index);
                    visited[index] = true;
                    while (queue.Count > 0)
                    {
                        int current = queue.Dequeue();
                        int currentX = current % mapWidth;
                        int currentY = current / mapWidth;
                        componentSize++;
                        minX = Math.Min(minX, currentX);
                        minY = Math.Min(minY, currentY);
                        maxX = Math.Max(maxX, currentX);
                        maxY = Math.Max(maxY, currentY);
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0)
                                {
                                    continue;
                                }
                                int nx = currentX + dx;
                                int ny = currentY + dy;
                                if (nx < 0 || ny < 0 || nx >= mapWidth || ny >= mapHeight)
                                {
                                    continue;
                                }
                                int neighbor = ny * mapWidth + nx;
                                if (visited[neighbor] ||
                                    Probability(output.Values[valueOffset + neighbor], outputIsLogits) < threshold)
                                {
                                    continue;
                                }
                                visited[neighbor] = true;
                                queue.Enqueue(neighbor);
                            }
                        }
                    }

                    if (componentSize < 2 || (maxX - minX < 1 && maxY - minY < 1))
                    {
                        continue;
                    }
                    boxes.Add(ScaleBox(
                        new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1),
                        mapWidth,
                        mapHeight,
                        inputWidth,
                        inputHeight,
                        sourceSize));
                    if (boxes.Count >= 128)
                    {
                        return SortBoxes(boxes);
                    }
                }
            }
            return SortBoxes(boxes);
        }

        private static List<Rectangle> TryExtractDirectBoxes(
            VisionTensorData output,
            int inputWidth,
            int inputHeight,
            Size sourceSize)
        {
            int[] dimensions = output.Dimensions ?? new int[0];
            if (dimensions.Length == 0)
            {
                return new List<Rectangle>();
            }
            int last = dimensions[dimensions.Length - 1];
            if (last != 4 && last != 8)
            {
                return new List<Rectangle>();
            }

            List<Rectangle> boxes = new List<Rectangle>();
            for (int offset = 0; offset + last <= output.Values.Length; offset += last)
            {
                float[] values = new float[last];
                Array.Copy(output.Values, offset, values, 0, last);
                double minX;
                double minY;
                double maxX;
                double maxY;
                if (last == 4)
                {
                    minX = Math.Min(values[0], values[2]);
                    minY = Math.Min(values[1], values[3]);
                    maxX = Math.Max(values[0], values[2]);
                    maxY = Math.Max(values[1], values[3]);
                }
                else
                {
                    minX = double.MaxValue;
                    minY = double.MaxValue;
                    maxX = double.MinValue;
                    maxY = double.MinValue;
                    for (int point = 0; point < 4; point++)
                    {
                        minX = Math.Min(minX, values[point * 2]);
                        minY = Math.Min(minY, values[point * 2 + 1]);
                        maxX = Math.Max(maxX, values[point * 2]);
                        maxY = Math.Max(maxY, values[point * 2 + 1]);
                    }
                }

                if (maxX <= minX || maxY <= minY)
                {
                    continue;
                }
                bool normalized = maxX <= 1.001D && maxY <= 1.001D;
                double xScale = normalized ? sourceSize.Width : sourceSize.Width / (double)inputWidth;
                double yScale = normalized ? sourceSize.Height : sourceSize.Height / (double)inputHeight;
                Rectangle box = Rectangle.FromLTRB(
                    Clamp((int)Math.Round(minX * xScale), 0, Math.Max(0, sourceSize.Width - 1)),
                    Clamp((int)Math.Round(minY * yScale), 0, Math.Max(0, sourceSize.Height - 1)),
                    Clamp((int)Math.Round(maxX * xScale), 1, sourceSize.Width),
                    Clamp((int)Math.Round(maxY * yScale), 1, sourceSize.Height));
                if (box.Width > 1 && box.Height > 1)
                {
                    boxes.Add(box);
                }
            }
            return SortBoxes(boxes);
        }

        private static VisionRecognitionData DecodeRecognition(
            VisionTensorData output,
            IList<string> keys,
            bool outputIsLogits)
        {
            if (output == null || output.Dimensions == null || output.Dimensions.Length < 2 ||
                keys == null || keys.Count == 0)
            {
                return new VisionRecognitionData(string.Empty, 0D);
            }

            int[] dimensions = output.Dimensions;
            int last = dimensions[dimensions.Length - 1];
            int previous = dimensions[dimensions.Length - 2];
            bool classLast = last >= keys.Count && last <= keys.Count + 2;
            if (!classLast && previous >= keys.Count && previous <= keys.Count + 2)
            {
                classLast = false;
            }
            else if (!classLast)
            {
                classLast = last >= previous;
            }

            int classCount = classLast ? last : previous;
            int sequenceLength = classLast ? previous : last;
            if (classCount <= 1 || sequenceLength <= 0 ||
                classCount * (long)sequenceLength > output.Values.Length)
            {
                return new VisionRecognitionData(string.Empty, 0D);
            }

            int baseOffset = output.Values.Length - classCount * sequenceLength;
            // PaddleOCR's CTC decoder reserves class 0 for blank while keys.txt
            // contains only actual characters. Keep support for compact models
            // that put blank at the last class when no implicit blank is present.
            bool implicitBlankAtZero = classCount > keys.Count;
            int blankClass = implicitBlankAtZero ? 0 : classCount - 1;
            int keyOffset = implicitBlankAtZero ? 1 : 0;
            int previousClass = -1;
            StringBuilder text = new StringBuilder();
            List<double> confidenceValues = new List<double>();
            for (int sequence = 0; sequence < sequenceLength; sequence++)
            {
                int bestClass = 0;
                double bestValue = double.MinValue;
                double maxValue = double.MinValue;
                for (int candidate = 0; candidate < classCount; candidate++)
                {
                    int offset = classLast
                        ? baseOffset + sequence * classCount + candidate
                        : baseOffset + candidate * sequenceLength + sequence;
                    double value = output.Values[offset];
                    if (value > bestValue)
                    {
                        bestValue = value;
                        bestClass = candidate;
                    }
                    maxValue = Math.Max(maxValue, value);
                }

                double confidence;
                if (!outputIsLogits)
                {
                    double probabilitySum = 0D;
                    bool probabilitiesAreUsable = true;
                    for (int candidate = 0; candidate < classCount; candidate++)
                    {
                        int offset = classLast
                            ? baseOffset + sequence * classCount + candidate
                            : baseOffset + candidate * sequenceLength + sequence;
                        double probability = output.Values[offset];
                        if (double.IsNaN(probability) || double.IsInfinity(probability) ||
                            probability < 0D || probability > 1D)
                        {
                            probabilitiesAreUsable = false;
                            break;
                        }
                        probabilitySum += probability;
                    }
                    confidence = probabilitiesAreUsable && probabilitySum > 0D
                        ? bestValue / probabilitySum
                        : double.NaN;
                }
                else
                {
                    double sum = 0D;
                    for (int candidate = 0; candidate < classCount; candidate++)
                    {
                        int offset = classLast
                            ? baseOffset + sequence * classCount + candidate
                            : baseOffset + candidate * sequenceLength + sequence;
                        sum += Math.Exp(output.Values[offset] - maxValue);
                    }
                    confidence = sum <= 0D
                        ? 0D
                        : Math.Exp(bestValue - maxValue) / sum;
                }
                if (double.IsNaN(confidence) || double.IsInfinity(confidence))
                {
                    confidence = 0D;
                }
                if (bestClass == blankClass)
                {
                    previousClass = -1;
                    continue;
                }
                if (bestClass == previousClass)
                {
                    continue;
                }
                previousClass = bestClass;
                int keyIndex = bestClass - keyOffset;
                if (keyIndex >= 0 && keyIndex < keys.Count)
                {
                    text.Append(keys[keyIndex]);
                    confidenceValues.Add(confidence);
                }
            }

            double average = confidenceValues.Count == 0
                ? 0D
                : confidenceValues.Sum() / confidenceValues.Count;
            return new VisionRecognitionData(text.ToString(), average);
        }

        private static VisionImageInputSpec ResolveInputSpec(
            InferenceSession session,
            string inputName,
            Size sourceSize,
            int maxImageSide,
            int defaultHeight,
            int defaultWidth)
        {
            int[] dimensions = session.InputMetadata[inputName].Dimensions.ToArray();
            if (dimensions.Length != 4)
            {
                throw new InvalidDataException(
                    string.Format("Unsupported ONNX image input rank: {0}; expected rank 4.", dimensions.Length));
            }
            bool channelsLast = IsChannelsLast(dimensions);
            int channelIndex = channelsLast ? dimensions.Length - 1 : dimensions.Length - 3;
            int heightIndex = channelsLast ? dimensions.Length - 3 : dimensions.Length - 2;
            int widthIndex = channelsLast ? dimensions.Length - 2 : dimensions.Length - 1;
            int channels = channelIndex >= 0 && channelIndex < dimensions.Length
                ? dimensions[channelIndex]
                : -1;
            int sourceHeight = heightIndex >= 0 && heightIndex < dimensions.Length
                ? dimensions[heightIndex]
                : -1;
            int sourceWidth = widthIndex >= 0 && widthIndex < dimensions.Length
                ? dimensions[widthIndex]
                : -1;
            if (channels <= 0)
            {
                channels = 3;
            }
            if (channels != 1 && channels != 3 && channels != 4)
            {
                throw new InvalidDataException(
                    string.Format("Unsupported ONNX image channel count: {0}.", channels));
            }

            int height = sourceHeight;
            int width = sourceWidth;
            if (height <= 0)
            {
                height = defaultHeight;
            }
            if (width <= 0)
            {
                width = defaultWidth;
            }
            if (sourceHeight <= 0 && sourceWidth <= 0)
            {
                double scale = Math.Min(1D, maxImageSide / (double)Math.Max(sourceSize.Width, sourceSize.Height));
                width = Math.Max(32, (int)Math.Round(sourceSize.Width * scale));
                height = Math.Max(32, (int)Math.Round(sourceSize.Height * scale));
            }
            else if (sourceHeight <= 0)
            {
                height = Math.Max(32, (int)Math.Round(sourceSize.Height * width / (double)Math.Max(1, sourceSize.Width)));
            }
            else if (sourceWidth <= 0)
            {
                width = Math.Max(32, (int)Math.Round(sourceSize.Width * height / (double)Math.Max(1, sourceSize.Height)));
            }
            return new VisionImageInputSpec(width, height, channels, channelsLast);
        }

        private static bool IsChannelsLast(int[] dimensions)
        {
            if (dimensions == null || dimensions.Length < 3)
            {
                return false;
            }
            int last = dimensions[dimensions.Length - 1];
            int nchwChannels = dimensions.Length == 3
                ? dimensions[0]
                : dimensions[dimensions.Length - 3];
            return (last == 1 || last == 3 || last == 4) &&
                nchwChannels != 1 && nchwChannels != 3 && nchwChannels != 4;
        }

        private static void WriteTensorValue(
            float[] values,
            VisionImageInputSpec inputSpec,
            VisionImagePreprocessProfile preprocess,
            int pixelOffset,
            int channel,
            float red,
            float green,
            float blue,
            float alpha)
        {
            float orderedValue = preprocess.GetOrderedValue(channel, red, green, blue, alpha);
            float normalized = preprocess.Normalize(orderedValue, channel);
            int index = inputSpec.ChannelsLast
                ? pixelOffset * inputSpec.Channels + channel
                : channel * inputSpec.Width * inputSpec.Height + pixelOffset;
            values[index] = normalized;
        }

        private sealed class VisionImageInputSpec
        {
            public VisionImageInputSpec(
                int width,
                int height,
                int channels,
                bool channelsLast)
            {
                this.Width = width;
                this.Height = height;
                this.Channels = channels;
                this.ChannelsLast = channelsLast;
            }

            public int Width { get; private set; }

            public int Height { get; private set; }

            public int Channels { get; private set; }

            public bool ChannelsLast { get; private set; }
        }

        private sealed class VisionImagePreprocessProfile
        {
            private VisionImagePreprocessProfile(
                string colorOrder,
                float[] mean,
                float[] standardDeviation,
                bool outputIsLogits)
            {
                this.ColorOrder = colorOrder;
                this.Mean = mean;
                this.StandardDeviation = standardDeviation;
                this.OutputIsLogits = outputIsLogits;
            }

            public string ColorOrder { get; private set; }

            public float[] Mean { get; private set; }

            public float[] StandardDeviation { get; private set; }

            public bool OutputIsLogits { get; private set; }

            public static VisionImagePreprocessProfile Load(
                string directory,
                string sectionName)
            {
                string path = FindModel(directory, "preprocess.json", "ocr_preprocess.json");
                if (path == null)
                {
                    return CreateDefault();
                }

                JObject root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                JToken section = root[sectionName] ?? root;
                string colorOrder = section.Value<string>("colorOrder");
                if (string.IsNullOrWhiteSpace(colorOrder))
                {
                    colorOrder = "RGB";
                }
                colorOrder = colorOrder.Trim().ToUpperInvariant();
                if (colorOrder != "RGB" && colorOrder != "BGR" && colorOrder != "GRAY")
                {
                    throw new InvalidDataException(
                        "Unsupported ONNX preprocessing colorOrder: " + colorOrder);
                }

                return new VisionImagePreprocessProfile(
                    colorOrder,
                    ReadArray(section["mean"], new[] { 0.5F, 0.5F, 0.5F }),
                    ReadArray(section["std"], new[] { 0.5F, 0.5F, 0.5F }),
                    section.Value<bool?>("outputIsLogits") ?? false);
            }

            public float GetOrderedValue(
                int channel,
                float red,
                float green,
                float blue,
                float alpha)
            {
                if (this.ColorOrder == "GRAY")
                {
                    return red * 0.299F + green * 0.587F + blue * 0.114F;
                }
                if (channel == 0)
                {
                    return this.ColorOrder == "BGR" ? blue : red;
                }
                if (channel == 1)
                {
                    return green;
                }
                if (channel == 2)
                {
                    return this.ColorOrder == "BGR" ? red : blue;
                }
                return alpha;
            }

            public float Normalize(float value, int channel)
            {
                int meanIndex = this.Mean.Length == 1
                    ? 0
                    : Math.Min(channel, this.Mean.Length - 1);
                int standardDeviationIndex = this.StandardDeviation.Length == 1
                    ? 0
                    : Math.Min(channel, this.StandardDeviation.Length - 1);
                float standardDeviation = this.StandardDeviation[standardDeviationIndex];
                if (Math.Abs(standardDeviation) < 0.000001F)
                {
                    throw new InvalidDataException("ONNX preprocessing std values must be non-zero.");
                }
                return (value - this.Mean[meanIndex]) / standardDeviation;
            }

            private static VisionImagePreprocessProfile CreateDefault()
            {
                return new VisionImagePreprocessProfile(
                    "RGB",
                    new[] { 0.5F, 0.5F, 0.5F },
                    new[] { 0.5F, 0.5F, 0.5F },
                    false);
            }

            private static float[] ReadArray(JToken token, float[] fallback)
            {
                if (token == null || token.Type != JTokenType.Array)
                {
                    return fallback;
                }

                List<float> values = new List<float>();
                foreach (JToken item in token)
                {
                    values.Add(item.Value<float>());
                }
                if (values.Count != 1 && values.Count != 3 && values.Count != 4)
                {
                    throw new InvalidDataException(
                        "ONNX preprocessing mean/std must contain 1, 3, or 4 values.");
                }
                return values.ToArray();
            }
        }

        private static Bitmap Crop(Bitmap source, Rectangle bounds)
        {
            Rectangle safe = Rectangle.Intersect(
                new Rectangle(Point.Empty, source.Size),
                bounds);
            if (safe.Width <= 0 || safe.Height <= 0)
            {
                throw new ArgumentException("The detected OCR box is outside the source image.", "bounds");
            }
            Bitmap crop = new Bitmap(safe.Width, safe.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(crop))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, safe.Width, safe.Height),
                    safe,
                    GraphicsUnit.Pixel);
            }
            return crop;
        }

        private static Rectangle ScaleBox(
            Rectangle box,
            int mapWidth,
            int mapHeight,
            int inputWidth,
            int inputHeight,
            Size sourceSize)
        {
            double inputX = inputWidth / (double)mapWidth;
            double inputY = inputHeight / (double)mapHeight;
            return Rectangle.FromLTRB(
                Clamp((int)Math.Floor(box.Left * inputX * sourceSize.Width / inputWidth), 0, Math.Max(0, sourceSize.Width - 1)),
                Clamp((int)Math.Floor(box.Top * inputY * sourceSize.Height / inputHeight), 0, Math.Max(0, sourceSize.Height - 1)),
                Clamp((int)Math.Ceiling(box.Right * inputX * sourceSize.Width / inputWidth), 1, sourceSize.Width),
                Clamp((int)Math.Ceiling(box.Bottom * inputY * sourceSize.Height / inputHeight), 1, sourceSize.Height));
        }

        private static List<Rectangle> SortBoxes(List<Rectangle> boxes)
        {
            boxes.Sort((left, right) =>
            {
                int top = left.Top.CompareTo(right.Top);
                return top != 0 ? top : left.Left.CompareTo(right.Left);
            });
            return boxes;
        }

        private static double Probability(float value, bool outputIsLogits)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0D;
            }
            if (!outputIsLogits && value >= 0F && value <= 1F)
            {
                return value;
            }
            return 1D / (1D + Math.Exp(-value));
        }

        private static string ResolveModelDirectory(string configuredDirectory)
        {
            string directory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? "models\\ocr"
                : configuredDirectory.Trim();
            if (Path.IsPathRooted(directory))
            {
                return Path.GetFullPath(directory);
            }
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory));
        }

        private static string FindModel(string directory, params string[] names)
        {
            foreach (string name in names)
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string BuildModelSignature(string directory)
        {
            string[] names =
            {
                "det.onnx",
                "dbnet.onnx",
                "rec.onnx",
                "crnn_lite_lstm.onnx",
                "keys.txt",
                "character_dict.txt",
                "preprocess.json",
                "ocr_preprocess.json"
            };
            List<string> entries = new List<string>();
            foreach (string name in names)
            {
                string path = Path.Combine(directory, name);
                if (!File.Exists(path))
                {
                    entries.Add(name + ":missing");
                    continue;
                }
                FileInfo info = new FileInfo(path);
                entries.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}:{2}",
                    name,
                    info.Length,
                    info.LastWriteTimeUtc.Ticks));
            }
            return string.Join("|", entries.ToArray());
        }

        private static IList<string> ReadKeys(string path)
        {
            List<string> keys = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string key = (line ?? string.Empty).TrimEnd('\r');
                if (key.Length > 0 && key[0] == '\uFEFF')
                {
                    key = key.Substring(1);
                }
                if (key.Length > 0)
                {
                    keys.Add(key);
                }
            }
            return keys;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private sealed class VisionOnnxModelSet : IDisposable
        {
            public VisionOnnxModelSet(
                string detectionPath,
                string recognitionPath,
                IList<string> keys)
            {
                string directory = Path.GetDirectoryName(detectionPath);
                this.DetectionPreprocess = VisionImagePreprocessProfile.Load(directory, "detector");
                this.RecognitionPreprocess = VisionImagePreprocessProfile.Load(directory, "recognizer");
                this.DetectionSession = new InferenceSession(detectionPath);
                try
                {
                    this.RecognitionSession = new InferenceSession(recognitionPath);
                }
                catch
                {
                    this.DetectionSession.Dispose();
                    throw;
                }
                this.DetectionInputName = ResolveImageInputName(this.DetectionSession, "detection");
                this.RecognitionInputName = ResolveImageInputName(this.RecognitionSession, "recognition");
                this.Keys = keys;
            }

            private static string ResolveImageInputName(
                InferenceSession session,
                string modelRole)
            {
                List<string> names = session.InputMetadata.Keys.ToList();
                if (names.Count == 1)
                {
                    return names[0];
                }

                string[] preferredNames = { "image", "images", "img", "input", "x" };
                foreach (string preferredName in preferredNames)
                {
                    string match = names.FirstOrDefault(
                        name => string.Equals(name, preferredName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        return match;
                    }
                }

                throw new InvalidDataException(string.Format(
                    CultureInfo.InvariantCulture,
                    "The ONNX {0} model has {1} inputs; configure a unique image input name.",
                    modelRole,
                    names.Count));
            }

            public InferenceSession DetectionSession { get; private set; }

            public InferenceSession RecognitionSession { get; private set; }

            public string DetectionInputName { get; private set; }

            public string RecognitionInputName { get; private set; }

            public IList<string> Keys { get; private set; }

            public VisionImagePreprocessProfile DetectionPreprocess { get; private set; }

            public VisionImagePreprocessProfile RecognitionPreprocess { get; private set; }

            public void Dispose()
            {
                if (this.DetectionSession != null)
                {
                    this.DetectionSession.Dispose();
                    this.DetectionSession = null;
                }
                if (this.RecognitionSession != null)
                {
                    this.RecognitionSession.Dispose();
                    this.RecognitionSession = null;
                }
            }
        }

        private sealed class VisionTensorData
        {
            public VisionTensorData(int[] dimensions, float[] values)
            {
                this.Dimensions = dimensions;
                this.Values = values;
            }

            public int[] Dimensions { get; private set; }

            public float[] Values { get; private set; }
        }

        private sealed class VisionRecognitionData
        {
            public VisionRecognitionData(string text, double confidence)
            {
                this.Text = text ?? string.Empty;
                this.Confidence = confidence;
            }

            public string Text { get; private set; }

            public double Confidence { get; private set; }
        }
    }
}
