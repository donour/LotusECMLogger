using SAE.J2534;

namespace LotusECMLogger.Services
{
    // TODO: Eliminate this compatibility shim. It exists only to preserve the throw-on-error
    // semantics of J2534-Sharp v1 after migrating to the result-based J2534-Sharp.Core v2 API.
    // The intended long-term direction is to embrace v2's result model directly at every call
    // site — inspect J2534Result/J2534Result<T>/GetMessagesResult via .IsSuccess/.Status/.Value
    // (and .IsTimeout for reads) and handle failures explicitly instead of throwing/catching.
    // Once all callers do that, delete this file: remove the Unwrap()/ThrowIfError() calls and
    // replace the catch (J2534Exception) sites (currently in OBDLoggerControl) with result checks.

    /// <summary>
    /// Thrown when a J2534 PassThru operation returns a non-success <see cref="ResultCode"/>.
    /// </summary>
    /// <remarks>
    /// J2534-Sharp v1 threw <c>SAE.J2534.J2534Exception</c> on hardware errors. The v2
    /// package (J2534-Sharp.Core) replaced that with a result-based API
    /// (<see cref="J2534Result"/>/<see cref="J2534Result{T}"/>). This type, combined with
    /// <see cref="J2534ResultExtensions"/>, restores the throw-on-error behavior the rest
    /// of the project relies on so existing <c>catch (J2534Exception)</c> sites keep working.
    /// </remarks>
    public sealed class J2534Exception : Exception
    {
        public ResultCode Status { get; }

        public J2534Exception(ResultCode status, string message)
            : base(string.IsNullOrEmpty(message) ? $"J2534 error: {status}" : message)
        {
            Status = status;
        }
    }

    /// <summary>
    /// Converts J2534-Sharp v2 result types back into the throw-on-error style used
    /// throughout this project.
    /// </summary>
    internal static class J2534ResultExtensions
    {
        /// <summary>Returns the result value, or throws <see cref="J2534Exception"/> on error.</summary>
        public static T Unwrap<T>(this J2534Result<T> result)
            => result.IsSuccess
                ? result.Value
                : throw new J2534Exception(result.Status, result.ErrorMessage);

        /// <summary>Throws <see cref="J2534Exception"/> if the operation failed.</summary>
        public static void ThrowIfError(this J2534Result result)
        {
            if (result.IsError)
                throw new J2534Exception(result.Status, result.ErrorMessage);
        }

        /// <summary>Throws <see cref="J2534Exception"/> if the operation failed (discarding the value).</summary>
        public static void ThrowIfError<T>(this J2534Result<T> result)
        {
            if (result.IsError)
                throw new J2534Exception(result.Status, result.ErrorMessage);
        }
    }
}
