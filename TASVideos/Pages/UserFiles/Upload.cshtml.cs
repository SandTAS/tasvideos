﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

using TASVideos.Data;
using TASVideos.Data.Entity;
using TASVideos.Extensions;
using TASVideos.MovieParsers;
using TASVideos.Pages.UserFiles.Models;
using TASVideos.Services;

namespace TASVideos.Pages.UserFiles
{
	[RequirePermission(PermissionTo.UploadUserFiles)]
	public class UploadModel : BasePageModel
	{
		private static readonly string[] SupportedCompressionTypes = { ".zip" }; // TODO: remaining format types
		private static readonly string[] SupportedSupplementalTypes = { ".lua", ".wch", ".gst" };

		private readonly ApplicationDbContext _db;
		private readonly MovieParser _parser;
		private readonly IFileService _fileService;

		public UploadModel(
			ApplicationDbContext db,
			MovieParser parser,
			IFileService fileService)
		{
			_db = db;
			_parser = parser;
			_fileService = fileService;
		}

		[BindProperty]
		public UserFileUploadModel UserFile { get; set; }

		public int StorageUsed { get; set; } 

		public IEnumerable<SelectListItem> AvailableSystems { get; set; } = new List<SelectListItem>();

		public IEnumerable<SelectListItem> AvailableGames { get; set; } = new List<SelectListItem>();

		public async Task OnGet()
		{
			await Initialize();
		}

		public async Task<IActionResult> OnPost()
		{
			if (!ModelState.IsValid)
			{
				await Initialize();
				return Page();
			}

			var fileExt = Path.GetExtension(UserFile.File.FileName);


			if (!SupportedCompressionTypes.Contains(fileExt)
				&& !SupportedCompressionTypes.Contains(fileExt)
				&& !_parser.SupportedMovieExtensions.Contains(fileExt))
			{
				ModelState.AddModelError(
					$"{nameof(UserFile)}.{nameof(UserFile.File)}",
					$"Unsupported file type: {fileExt}");
				await Initialize();
				return Page();
			}

			// TODO: decompress if zip type
			var fileBytes = await FormFileToBytes(UserFile.File);
			var fileName = UserFile.File.FileName;

			if (SupportedCompressionTypes.Contains(fileExt))
			{
				// TODO
				ModelState.AddModelError(
					$"{nameof(UserFile)}.{nameof(UserFile.File)}",
					$"Compressed files not yet supported");
				await Initialize();
				return Page();
			}

			if (SupportedSupplementalTypes.Contains(fileExt))
			{
				// TODO
				ModelState.AddModelError(
					$"{nameof(UserFile)}.{nameof(UserFile.File)}",
					$"Supplamental files not yet supported");
				await Initialize();
				return Page();
			}

			UserFile userFile = new UserFile
			{
				Id = DateTime.UtcNow.Ticks,
				Title = UserFile.Title,
				Description = UserFile.Description,
				SystemId = UserFile.SystemId,
				GameId = UserFile.GameId,
				AuthorId = User.GetUserId(),
				LogicalLength = (int)UserFile.File.Length,
				UploadTimestamp = DateTime.UtcNow,
				Class = SupportedSupplementalTypes.Contains(fileExt)
					? UserFileClass.Support
					: UserFileClass.Movie,
				Type = fileExt.Replace(".", ""),
				
				FileName = UserFile.File.FileName
			};

			var supportedExtensions = _parser.SupportedMovieExtensions;
			if (_parser.SupportedMovieExtensions.Contains(fileExt))
			{
				var parseResult = _parser.ParseFile(UserFile.File.FileName, UserFile.File.OpenReadStream());
				if (!parseResult.Success)
				{
					ModelState.AddModelError(
						$"{nameof(UserFile)}.{nameof(UserFile.File)}",
						"Error parsing movie file");
					await Initialize();
					return Page();
				}

				userFile.Rerecords = parseResult.RerecordCount;
				userFile.Frames = parseResult.Frames;
				// TODO: length
			}

			var fileResult = await _fileService.Compress(fileBytes);

			userFile.PhysicalLength = fileResult.CompressedSize;
			userFile.CompressionType = fileResult.Type;
			userFile.Content = fileResult.Data;

			_db.UserFiles.Add(userFile);
			await _db.SaveChangesAsync();

			return RedirectToPage("/Profile/UserFiles");
		}

		private async Task Initialize()
		{
			var userId = User.GetUserId();
			StorageUsed = await _db.UserFiles
				.Where(uf => uf.AuthorId == userId)
				.SumAsync(uf => uf.LogicalLength);

			AvailableSystems = UiDefaults.DefaultEntry.Concat(await _db.GameSystems
				.Select(s => new SelectListItem
				{
					Value = s.Id.ToString(),
					Text = s.Code
				})
				.ToListAsync());

			AvailableGames = UiDefaults.DefaultEntry.Concat(await _db.Games
				.OrderBy(g => g.SystemId)
				.ThenBy(g => g.DisplayName)
				.ToDropDown()
				.ToListAsync());
		}
	}
}