using Microsoft.AspNetCore.Mvc;
using ST10439147_CLDV6212_POE.Models;
using ST10439147_CLDV6212_POE.Services;

namespace ST10439147_CLDV6212_POE.Controllers
{
    public class FileUploadController : Controller
    {
        private readonly AzureService _storageService;

        public FileUploadController(AzureService storageService)
        {
            _storageService = storageService;
        }

        public async Task<IActionResult> Index()
        {
            var files = await _storageService.GetAllFilesAsync();
            return View(files);
        }

        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file != null && file.Length > 0)
            {
                var fileName = await _storageService.UploadFileAsync(file);
                TempData["Success"] = $"File {fileName} uploaded successfully!";
                return RedirectToAction(nameof(Index));
            }

            TempData["Error"] = "Please select a file to upload.";
            return View();
        }

        public async Task<IActionResult> Download(string fileName)
        {
            try
            {
                var fileStream = await _storageService.DownloadFileAsync(fileName);
                return File(fileStream, "application/octet-stream", fileName);
            }
            catch
            {
                TempData["Error"] = "File not found.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
