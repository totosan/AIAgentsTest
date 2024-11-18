using System;
using System.ComponentModel;
using System.IO;
using System.Management.Automation;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace MultiAgent.Plugins
{
    public sealed class FilePlugin
    {
        [KernelFunction, Description("Reads a given file.")]
        public static async Task<string> ReadFileAsync(string filename)
        {
            //if relative path is provided, convert it to absolute path
            if (!Path.IsPathRooted(filename))
            {
                filename = Path.GetFullPath(filename);
            }
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentException("File path cannot be null or empty", nameof(filename));
            }

            if (!File.Exists(filename))
            {
                throw new FileNotFoundException("File not found", filename);
            }

            using (StreamReader reader = new StreamReader(filename))
            {
                return await reader.ReadToEndAsync();
            }
        }

        [KernelFunction, Description("Writes to a given file.")]
        public static async Task WriteFileAsync(string filename, string content)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentException("File path cannot be null or empty", nameof(filename));
            }

            using (StreamWriter writer = new StreamWriter(filename, false))
            {
                await writer.WriteAsync(content);
            }
        }

        [KernelFunction, Description("Deletes a file.")]
        public static void DeleteFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentException("File path cannot be null or empty", nameof(filename));
            }

            if (File.Exists(filename))
            {
                File.Delete(filename);
            }
            else
            {
                throw new FileNotFoundException("File not found", filename);
            }
        }

        //rename a file
        [KernelFunction, Description("Renames a file. It takes the old file path and uses the given new filename. It returns DONE if the file is renamed successfully or the exception message if an error occurs.")]
        public static string RenameFile(string oldFilePath, string newFilename)
        {
            if (string.IsNullOrEmpty(oldFilePath))
            {
                return new ArgumentException("Old file path cannot be null or empty", nameof(oldFilePath)).Message;
            }

            if (string.IsNullOrEmpty(newFilename))
            {
                return new ArgumentException("New file path cannot be null or empty", nameof(newFilename)).Message;
            }

            // if the newFilename is a relative path, convert it to absolute path
            if (!Path.IsPathRooted(newFilename))
            {
                newFilename = Path.GetDirectoryName(Path.GetFullPath(oldFilePath)) + Path.DirectorySeparatorChar + newFilename;
            }
            // check if file hast pdf extension. If not, add it
            if (Path.GetExtension(newFilename) != ".pdf")
            {
                newFilename += ".pdf";
            }
            if (File.Exists(oldFilePath))
            {
                if (File.Exists(newFilename))
                {
                    return new IOException($"A file with the name {newFilename} already exists.").Message;
                }
                File.Move(oldFilePath, newFilename);
                return "DONE";
            }
            else
            {
                return new FileNotFoundException("File not found", oldFilePath).Message;
            }
        }
    }
}