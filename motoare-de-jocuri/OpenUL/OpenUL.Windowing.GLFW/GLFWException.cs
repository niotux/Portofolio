using Lib = Silk.NET.GLFW;

namespace OpenUL.Windowing.GLFW;

public class GLFWException : Exception
{
  public GLFWErrorCode ErrorCode { get; }

  public GLFWException(string? message, GLFWErrorCode code)
  : base(message)
  {
    ErrorCode = code;
  }

  public GLFWException(string? message, GLFWErrorCode code, Exception innerException)
  : base(message, innerException)
  {
    ErrorCode = code;
  }

  public GLFWException()
  : base() { }

}

public enum GLFWErrorCode
{
  NoError = default,
  NotInitialized = 65537,
  NoContext = 65538,
  InvalidEnum = 65539,
  InvalidValue = 65540,
  OutOfMemory = 65541,
  ApiUnavailable = 65542,
  VersionUnavailable = 65543,
  PlatformError = 65544,
  FormatUnavailable = 65545,
  NoWindowContext = 65546,
  CursorUnavailable = 65547,
  FeatureUnavailable = 65548,
  FeatureUnimplemented = 65549,
  PlatformUnavailable = 65550,
}