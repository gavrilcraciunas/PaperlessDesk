namespace PaperlessDesktop.Shared;

public class PaperlessException : Exception { public PaperlessException(string m) : base(m) { } }
public class OcrCancelledException : PaperlessException { public OcrCancelledException() : base("OCR was cancelled.") { } }
public class OcrException : PaperlessException { public OcrException(string m) : base(m) { } }
public class CompressionException : PaperlessException { public CompressionException(string m) : base(m) { } }
public class ConversionException : PaperlessException { public ConversionException(string m) : base(m) { } }
public class LicenseException : PaperlessException { public LicenseException(string m) : base(m) { } }
