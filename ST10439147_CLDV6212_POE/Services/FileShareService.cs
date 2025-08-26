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
        private readonly string _connectionString;// the connection string to the Azure Storage account
        private readonly ShareServiceClient _shareServiceClient;// the client to interact with the Azure File Share service
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        public FileShareService(IConfiguration configuration)
        {
            _connectionString = configuration["AzureStorage:ConnectionString"];// get the connection string from configuration
            _shareServiceClient = new ShareServiceClient(_connectionString);// initialize the ShareServiceClient with the connection string
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to upload a file to the Azure File Share
        // Returns the unique file name assigned to the uploaded file
        // Default share name is "dummycontracts"
        // If the share does not exist, it will be created
        // The file is uploaded to the root directory of the share
        // A unique file name is generated using a GUID to avoid name collisions
        // The file is read from the IFormFile stream and uploaded to the share
        public async Task<string> UploadFileAsync(IFormFile file, string shareName = "dummycontracts")// default share name is "dummycontracts"
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);// get the share client for the specified share name
            await shareClient.CreateIfNotExistsAsync();// create the share if it does not exist

            var directoryClient = shareClient.GetRootDirectoryClient();// get the root directory client
            var fileName = $"{Guid.NewGuid()}_{file.FileName}";// create a unique file name using a GUID
            var fileClient = directoryClient.GetFileClient(fileName);// get the file client for the specified file name

            using var stream = file.OpenReadStream();// open a stream to read the file
            await fileClient.CreateAsync(stream.Length);// create the file in the share with the specified length
            await fileClient.UploadAsync(stream);// upload the file to the share

            return fileName;// return the unique file name
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to upload a file to a specific directory within the Azure File Share
        // Returns the unique file name assigned to the uploaded file
        // Default share name is "dummycontracts"
        // If the share or directory does not exist, they will be created
        // The file is uploaded to the specified directory within the share
        // A unique file name is generated using a GUID to avoid name collisions
        // The file is read from the IFormFile stream and uploaded to the share
        // directoryPath: the path of the directory within the share where the file will be uploaded
        public async Task<string> UploadFileToDirectoryAsync(IFormFile file, string directoryPath, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);// get the share client for the specified share name
            await shareClient.CreateIfNotExistsAsync();// create the share if it does not exist

            var directoryClient = shareClient.GetDirectoryClient(directoryPath);
            await directoryClient.CreateIfNotExistsAsync();

            var fileName = $"{Guid.NewGuid()}_{file.FileName}";// create a unique file name using a GUID
            var fileClient = directoryClient.GetFileClient(fileName);

            using var stream = file.OpenReadStream();
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            return fileName;
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to retrieve all file names from the root directory of the specified Azure File Share
        // Default share name is "dummycontracts"
        // The method returns a list of file names as strings
        // It uses asynchronous enumeration to efficiently retrieve the file names
        // If the share or directory does not exist, an empty list is returned
        // The method filters out directories and only includes file names in the returned list
        // The method can be extended to include pagination or filtering options if needed
        // The method is useful for displaying a list of files available in the share for download or management
        // The method can be called from a controller or service to integrate with other application logic
        // The method leverages the Azure.Storage.Files.Shares library for interacting with the Azure File Share service
        // The method is designed to be efficient and scalable for handling large numbers of files
        public async Task<List<string>> GetAllFilesAsync(string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);// get the share client for the specified share name
            var directoryClient = shareClient.GetRootDirectoryClient();// get the root directory client
            var files = new List<string>();// list to hold file names

            await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())// asynchronous enumeration of files and directories
            {
                if (!item.IsDirectory)// filter out directories
                {
                    files.Add(item.Name);
                }
            }

            return files;
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to retrieve all file names from a specific directory within the specified Azure File Share
        // Default share name is "dummycontracts"
        // The method returns a list of file names as strings
        // It uses asynchronous enumeration to efficiently retrieve the file names
        // If the share or directory does not exist, an empty list is returned
        // The method filters out directories and only includes file names in the returned list
        // The method can be extended to include pagination or filtering options if needed
        // The method is useful for displaying a list of files available in the specified directory for download or management
        public async Task<List<string>> GetFilesInDirectoryAsync(string directoryPath, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);// get the share client for the specified share name
            var directoryClient = shareClient.GetDirectoryClient(directoryPath);// get the directory client for the specified directory path
            var files = new List<string>();// list to hold file names

            // asynchronous enumeration of files and directories, filtering out directories, and adding file names to the list
            await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                {
                    files.Add(item.Name);
                }
            }

            return files;
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to download a file from the root directory of the specified Azure File Share
        // Default share name is "dummycontracts"
        // The method returns a Stream representing the content of the downloaded file
        // It uses asynchronous operations to efficiently download the file
        // If the share, directory, or file does not exist, an exception will be thrown
        // The method can be called from a controller or service to integrate with other application logic
        public async Task<Stream> DownloadFileAsync(string fileName, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);// get the share client for the specified share name
            var directoryClient = shareClient.GetRootDirectoryClient();// get the root directory client
            var fileClient = directoryClient.GetFileClient(fileName);// get the file client for the specified file name

            var download = await fileClient.DownloadAsync();// download the file asynchronously
            return download.Value.Content;// return the content of the downloaded file as a Stream
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to download a file from a specific directory within the specified Azure File Share
        // Default share name is "dummycontracts"
        // The method returns a Stream representing the content of the downloaded file
        // It uses asynchronous operations to efficiently download the file
        // If the share, directory, or file does not exist, an exception will be thrown
        // The method can be called from a controller or service to integrate with other application logic
        // directoryPath: the path of the directory within the share where the file is located
        public async Task<Stream> DownloadFileFromDirectoryAsync(string fileName, string directoryPath, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetDirectoryClient(directoryPath);// get the directory client for the specified directory path
            var fileClient = directoryClient.GetFileClient(fileName);

            var download = await fileClient.DownloadAsync();// download the file asynchronously
            return download.Value.Content;
        }
        //--------------------------------------------------------------------------------------------------------------------------------------------------------------//
        // Method to delete a file from the root directory of the specified Azure File Share
        // Default share name is "dummycontracts"
        // The method returns a boolean indicating whether the file was successfully deleted
        // It uses asynchronous operations to efficiently delete the file
        // If the share, directory, or file does not exist, the method will return false
        // The method can be called from a controller or service to integrate with other application logic
        // The method is useful for managing files in the share and removing unnecessary or outdated files
        public async Task<bool> DeleteFileAsync(string fileName, string shareName = "dummycontracts")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = shareClient.GetRootDirectoryClient();// get the root directory client
            var fileClient = directoryClient.GetFileClient(fileName);

            var response = await fileClient.DeleteIfExistsAsync();// delete the file if it exists
            return response.Value;// return true if the file was deleted, false otherwise
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//