using System.Net;
namespace Venly.Backend.Common;
/// <summary>The three response shapes every handler builds, in one place.</summary>
public static class Reply
{
    public static RequestResponse<T> Ok<T>(T data, HttpStatusCode code = HttpStatusCode.OK) => new()
    {
        ResponseCode = (int)code,
        ResponseMessage = ResponseMessages.GetSuccessMessage(),
        ResponseData = data,
    };
    public static RequestResponse<T> Fail<T>(HttpStatusCode code, string message, List<string>? errors = null) => new()
    {
        ResponseCode = (int)code,
        ResponseMessage = message,
        Errors = errors,
    };
}
