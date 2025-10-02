// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 2

using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ST10439147_CLDV6212_POE.Extensions
{
    public static class HttpRequestDataExtensions
    {
        public static async Task<MultipartFormData> ReadMultipartAsync(this HttpRequestData req)
        {
            var result = new MultipartFormData();

            var contentType = req.Headers.TryGetValues("Content-Type", out var values)
                ? values.FirstOrDefault()
                : null;

            if (string.IsNullOrEmpty(contentType) || !contentType.Contains("multipart/form-data"))
            {
                throw new InvalidOperationException("Request must be multipart/form-data");
            }

            var boundary = GetBoundary(MediaTypeHeaderValue.Parse(contentType));
            var reader = new MultipartReader(boundary, req.Body);

            MultipartSection section;
            while ((section = await reader.ReadNextSectionAsync()) != null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
                    continue;

                if (HasFileContentDisposition(contentDisposition))
                {
                    var fileName = contentDisposition.FileName.Value?.Trim('"');
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        var memoryStream = new MemoryStream();
                        await section.Body.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;

                        result.Files.Add(new FileData
                        {
                            Name = contentDisposition.Name.Value?.Trim('"') ?? "file",
                            FileName = fileName,
                            ContentType = section.ContentType ?? "application/octet-stream",
                            Stream = memoryStream
                        });
                    }
                }
                else if (HasFormDataContentDisposition(contentDisposition))
                {
                    var key = contentDisposition.Name.Value?.Trim('"');
                    if (!string.IsNullOrEmpty(key))
                    {
                        using var streamReader = new StreamReader(section.Body);
                        var value = await streamReader.ReadToEndAsync();
                        result.Fields[key] = value;
                    }
                }
            }

            return result;
        }

        private static string GetBoundary(MediaTypeHeaderValue contentType)
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new InvalidOperationException("Missing content-type boundary.");
            }
            return boundary;
        }

        private static bool HasFileContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            return contentDisposition != null
                && contentDisposition.DispositionType.Equals("form-data")
                && !string.IsNullOrEmpty(contentDisposition.FileName.Value?.Trim('"'));
        }

        private static bool HasFormDataContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            return contentDisposition != null
                && contentDisposition.DispositionType.Equals("form-data")
                && string.IsNullOrEmpty(contentDisposition.FileName.Value);
        }
    }

    public class MultipartFormData
    {
        public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>();
        public List<FileData> Files { get; } = new List<FileData>();

        public string GetField(string key)
        {
            return Fields.ContainsKey(key) ? Fields[key] : string.Empty;
        }
    }

    public class FileData
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public Stream Stream { get; set; }
        public long Length => Stream?.Length ?? 0;

        public Stream OpenReadStream() => Stream;
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//