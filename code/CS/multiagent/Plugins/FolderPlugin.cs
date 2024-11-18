using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace MultiAgent.Plugins
{
    public sealed class FolderPlugin
    {
        [KernelFunction, Description("Creates a folder by the given name and path.")]
        public static async Task<bool> CreateFolderAsync(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating folder: {ex.Message}");
                return await Task.FromResult(false);
            }
        }

        [KernelFunction, Description("Deletes a folder by the given path.")]
        public static async Task<bool> DeleteFolderAsync(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting folder: {ex.Message}");
                return await Task.FromResult(false);
            }
        }

        [KernelFunction, Description("Lists all folders in the given path.")]
        public static async Task<string[]> ListFoldersAsync(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var directories = Directory.GetDirectories(path);
                    return await Task.FromResult(directories);
                }
                return await Task.FromResult(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing folders: {ex.Message}");
                return await Task.FromResult(Array.Empty<string>());
            }
        }

        [KernelFunction, Description("Lists all files in the given path.")]
        public static async Task<string[]> ListFilesAsync(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path);
                    return await Task.FromResult(files);
                }
                return await Task.FromResult(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing files: {ex.Message}");
                return await Task.FromResult(Array.Empty<string>());
            }
        }
    }
}