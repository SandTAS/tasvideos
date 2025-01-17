﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TASVideos.Data;
using TASVideos.Data.Entity;
using TASVideos.Pages.Games.Roms.Models;

namespace TASVideos.Pages.Games.Roms
{
	[RequirePermission(PermissionTo.CatalogMovies)]
	public class ListModel : BasePageModel
	{
		private readonly ApplicationDbContext _db;

		public ListModel(ApplicationDbContext db)
		{
			_db = db;
		}

		[FromRoute]
		public int GameId { get; set; }

		public RomListModel Roms { get; set; } = new ();

		public async Task<IActionResult> OnGet()
		{
			Roms = await _db.Games
				.Where(g => g.Id == GameId)
				.Select(g => new RomListModel
				{
					GameDisplayName = g.DisplayName,
					SystemCode = g.System!.Code,
					Roms = g.Roms
					.Select(r => new RomListModel.RomEntry
					{
						Id = r.Id,
						DisplayName = r.Name,
						Md5 = r.Md5,
						Sha1 = r.Sha1,
						Version = r.Version,
						Region = r.Region,
						RomType = r.Type
					})
					.ToList()
				})
				.SingleOrDefaultAsync();

			if (Roms == null)
			{
				return NotFound();
			}

			return Page();
		}
	}
}
