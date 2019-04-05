﻿using System;
using System.Collections.Generic;
using System.Linq;
using TASVideos.Data.Entity;

namespace TASVideos.Pages.Publications.Models
{
	public class PublicationDisplayModel
	{
		public int Id { get; set; }
		public DateTime CreateTimeStamp { get; set; }
		public DateTime LastUpdateTimeStamp { get; set; }
		public string LastUpdateUser { get; set; }

		public int? ObsoletedById { get; set; }
		public string Title { get; set; }
		public string TierIconPath { get; set; }
		public string MovieFileName { get; set; }
		public int SubmissionId { get; set; }
		public string OnlineWatchingUrl { get; set; }
		public string MirrorSiteUrl { get; set; }
		public int TopicId { get; set; }

		public IEnumerable<TagModel> Tags { get; set; } = new List<TagModel>();
		public IEnumerable<TagModel> GenreTags { get; set; } = new List<TagModel>();
		public IEnumerable<FileModel> Files { get; set; } = new List<FileModel>();
		public IEnumerable<FlagModel> Flags { get; set; } = new List<FlagModel>();

		public string Screenshot => Files.First(f => f.Type == FileType.Screenshot).Path;

		public IEnumerable<string> TorrentLinks => Files
			.Where(f => f.Type == FileType.Torrent)
			.Select(f => f.Path);

		public double RatingCount { get; set; }
		public double? OverallRating { get; set; }

		public class TagModel
		{
			public string DisplayName { get; set; }
			public string Code { get; set; }
		}

		public class FileModel
		{
			public string Path { get; set; }
			public FileType Type { get; set; }
		}

		public class FlagModel
		{
			public string IconPath { get; set; }
			public string LinkPath { get; set; }
			public string Name { get; set; }
		}
	}
}
