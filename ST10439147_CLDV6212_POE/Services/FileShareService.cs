// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 1

//References:
// ClaudAI - https://claude.ai/
// ChatGPT - https://chat.openai.com/
// W3schools - https://www.w3schools.com/
// IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
// AzureApp project done in class with lecturer

using Azure.Storage.Files.Shares;
using Microsoft.Extensions.Configuration;

namespace ST10439147_CLDV6212_POE.Services
{
    public class FileShareService
    {
        private readonly string _connectionString;
        private readonly ShareServiceClient _shareServiceClient;

        public FileShareService(IConfiguration configuration)
        {
            _connectionString = configuration["AzureStorage:ConnectionString"];
            _shareServiceClient = new ShareServiceClient(_connectionString);
        }

        public async Task<string> UploadFileAsync(IFormFile file, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            await shareClient.CreateIfNotExistsAsync();

            var directoryClient = shareClient.GetRootDirectoryClient();
            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var fileClient = directoryClient.GetFileClient(fileName);

            using var stream = file.OpenReadStream();
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            return fileName;
        }

        public async Task<string> UploadFileToDirectoryAsync(IFormFile file, string directoryPath, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            await shareClient.CreateIfNotExistsAsync();

            var directoryClient = shareClient.GetDirectoryClient(directoryPath);
            await directoryClient.CreateIfNotExistsAsync();

            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var fileClient = directoryClient.GetFileClient(fileName);

            using var stream = file.OpenReadStream();
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            return fileName;
        }

        public async Task<List<string>> GetAllFilesAsync(string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetRootDirectoryClient();
            var files = new List<string>();

            await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                {
                    files.Add(item.Name);
                }
            }

            return files;
        }

        public async Task<List<string>> GetFilesInDirectoryAsync(string directoryPath, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetDirectoryClient(directoryPath);
            var files = new List<string>();

            await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                {
                    files.Add(item.Name);
                }
            }

            return files;
        }

        public async Task<Stream> DownloadFileAsync(string fileName, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetRootDirectoryClient();
            var fileClient = directoryClient.GetFileClient(fileName);

            var download = await fileClient.DownloadAsync();
            return download.Value.Content;
        }

        public async Task<Stream> DownloadFileFromDirectoryAsync(string fileName, string directoryPath, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetDirectoryClient(directoryPath);
            var fileClient = directoryClient.GetFileClient(fileName);

            var download = await fileClient.DownloadAsync();
            return download.Value.Content;
        }

        public async Task<bool> DeleteFileAsync(string fileName, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetRootDirectoryClient();
            var fileClient = directoryClient.GetFileClient(fileName);

            var response = await fileClient.DeleteIfExistsAsync();
            return response.Value;
        }
    }
}