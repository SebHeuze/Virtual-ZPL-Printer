﻿/*
 *  This file is part of Virtual ZPL Printer.
 *  
 *  Virtual ZPL Printer is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Virtual ZPL Printer is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Virtual ZPL Printer.  If not, see <https://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageCache.Abstractions;
using Labelary.Abstractions;
using Newtonsoft.Json;

namespace ImageCache.Repository
{
	public class ImageCacheRepository : IImageCacheRepository
	{
		protected static string FileName(DirectoryInfo imagePathRoot, string baseName, int id) => $@"{imagePathRoot.FullName}\{Path.GetFileNameWithoutExtension(baseName)}-{id}.png";
		protected static string FileName(DirectoryInfo imagePathRoot, string baseName, int id, int page) => $@"{imagePathRoot.FullName}\{Path.GetFileNameWithoutExtension(baseName)}-{id}-Page{page}.png";
		public static string MetaDataFile(string imageFile) => $"{Path.GetDirectoryName(imageFile)}/{Path.GetFileNameWithoutExtension(imageFile)}.json";
		protected object LockObject { get; } = new object();
		protected int AlternateIndex = 99999;

		protected static FileInfo[] GetFiles(DirectoryInfo imagePathRoot)
		{
			FileInfo[] returnValue = Array.Empty<FileInfo>();

			returnValue = (from tbl in imagePathRoot.EnumerateFiles("*.png")
						   orderby tbl.CreationTime
						   select tbl).ToArray();

			return returnValue;
		}

		protected static int[] GetFileIndices(DirectoryInfo imagePathRoot)
		{
			int[] returnValue = Array.Empty<int>();

			returnValue = (from tbl in imagePathRoot.EnumerateFiles("*.png")
						   orderby tbl.CreationTime
						   select GetFileIndex(tbl)).ToArray();

			return returnValue;
		}

		protected static int GetFileIndex(FileInfo file)
		{
			int returnValue = 1;

			//
			// Split th file name.
			//
			string[] parts = Path.GetFileNameWithoutExtension(file.Name).Split(new char[] { '-' }, StringSplitOptions.TrimEntries & StringSplitOptions.RemoveEmptyEntries);

			if (parts.Last().Contains("Page"))
			{
				returnValue = Convert.ToInt32(parts[parts.Length - 2]);
			}
			else
			{
				returnValue = Convert.ToInt32(parts.Last());
			}

			return returnValue;
		}

		protected static DirectoryInfo GetDirectory(string imagePathRoot)
		{
			DirectoryInfo returnValue = null;

			returnValue = new(imagePathRoot);
			returnValue.Create();

			return returnValue;
		}

		protected static int GetNextIndex(DirectoryInfo imagePathRoot)
		{
			int returnValue = 1;

			int[] indices = GetFileIndices(imagePathRoot);

			if (indices.Any())
			{
				returnValue = indices.Max() + 1;
			}

			return returnValue;
		}

		public Task<IEnumerable<IStoredImage>> StoreLabelImagesAsync(string imagePathRoot, IEnumerable<IGetLabelResponse> labels)
		{
			IList<IStoredImage> returnValue = [];

			lock (this.LockObject)
			{
				//
				// Ensure the path exists.
				//
				DirectoryInfo dir = GetDirectory(imagePathRoot);

				//
				// Get the next ID.
				//
				int id = GetNextIndex(dir);

				foreach (IGetLabelResponse label in labels)
				{
					//
					// Get the file name.
					//
					string fileName = label.HasMultipleLabels ? FileName(dir, label.ImageFileName, id, label.LabelIndex + 1) : FileName(dir, label.ImageFileName, id);

					//
					// Write the image.
					//
					_ = File.WriteAllBytesAsync(fileName, label.Label);

					//
					// Write a text file if the image has warnings.
					//
					if (label.Warnings != null && label.Warnings.Any())
					{
						string json = JsonConvert.SerializeObject(label, Formatting.Indented);
						_ = File.WriteAllTextAsync(MetaDataFile(fileName), json);
					}

					IStoredImage storedImage = new StoredImage()
					{
						Id = id,
						FullPath = fileName
					};

					//
					// Add the item to the list.
					//
					returnValue.Add(storedImage);
				}
			}

			return Task.FromResult<IEnumerable<IStoredImage>>(returnValue);
		}

		public Task<IEnumerable<IStoredImage>> GetAllAsync(string imagePathRoot)
		{
			IList<IStoredImage> returnValue = [];

			DirectoryInfo dir = GetDirectory(imagePathRoot);

			if (dir.Exists)
			{
				FileInfo[] files = GetFiles(dir);

				foreach (FileInfo file in files.OrderBy(t => t.CreationTime))
				{
					StoredImage si = new();

					try
					{
						si.Id = GetFileIndex(file);
					}
					catch
					{
						si.Id = this.AlternateIndex--;
					}

					si.FullPath = file.FullName;

					returnValue.Add(si);
				}
			}

			return Task.FromResult<IEnumerable<IStoredImage>>(returnValue.OrderBy(t => t.Timestamp).ToArray());
		}

		public Task<bool> ClearAllAsync(string imagePathRoot)
		{
			bool returnValue = false;

			DirectoryInfo dir = GetDirectory(imagePathRoot);
			int errorCount = 0;

			if (dir.Exists)
			{
				FileInfo[] files = GetFiles(dir);

				foreach (FileInfo file in files)
				{
					try
					{
						file.Delete();

						//
						// Clear the text file too.
						//
						if (File.Exists(MetaDataFile(file.FullName)))
						{
							File.Delete(MetaDataFile(file.FullName));
						}
					}
					catch
					{
						errorCount++;
					}
				}

				returnValue = errorCount == 0;
			}

			return Task.FromResult(returnValue);
		}

		public Task<bool> DeleteImageAsync(string imagePathRoot, string imageName)
		{
			bool returnValue = false;

			DirectoryInfo dir = GetDirectory(imagePathRoot);

			if (dir.Exists)
			{
				FileInfo file = dir.GetFiles().Where(t => t.Name == imageName).SingleOrDefault();

				if (file != null)
				{
					file.Delete();

					//
					// Clear the text file too.
					//
					if (File.Exists(MetaDataFile(file.FullName)))
					{
						File.Delete(MetaDataFile(file.FullName));
					}

					returnValue = true;
				}
			}

			return Task.FromResult(returnValue);
		}
	}
}