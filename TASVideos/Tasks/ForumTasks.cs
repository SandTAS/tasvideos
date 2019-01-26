﻿using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

using TASVideos.Data;
using TASVideos.Data.Constants;
using TASVideos.Data.Entity;
using TASVideos.Data.Entity.Forum;
using TASVideos.Models;
using TASVideos.Services;

namespace TASVideos.Tasks
{
	public class ForumTasks
	{
		private readonly ApplicationDbContext _db;
		private readonly AwardTasks _awardTasks;
		private readonly IEmailService _emailService;

		public ForumTasks(
			ApplicationDbContext db,
			AwardTasks awardTasks,
			IEmailService emailService)
		{
			_db = db;
			_awardTasks = awardTasks;
			_emailService = emailService;
		}

		/// <summary>
		/// Returns the position of post is in its parent topic
		/// If a post with the given id can not be found, null is returned
		/// </summary>
		public async Task<PostPositionModel> GetPostPosition(int postId, bool seeRestricted)
		{
			var post = await _db.ForumPosts
				.ExcludeRestricted(seeRestricted)
				.SingleOrDefaultAsync(p => p.Id == postId);

			if (post == null)
			{
				return null;
			}

			var posts = await _db.ForumPosts
				.ForTopic(post.TopicId ?? -1)
				.OldestToNewest()
				.ToListAsync();

			var position = posts.IndexOf(post);
			return new PostPositionModel
			{
				Page = (position / ForumConstants.PostsPerPage) + 1,
				TopicId = post.TopicId ?? 0
			};
		}

		/// <summary>
		/// Displays a page of posts for the given topic
		/// </summary>
		public async Task<ForumTopicModel> GetTopicForDisplay(int id, TopicRequest paging, bool allowRestricted, int? userId)
		{
			var model = await _db.ForumTopics
				.ExcludeRestricted(allowRestricted)
				.Select(t => new ForumTopicModel
				{
					Id = t.Id,
					IsWatching = userId.HasValue && t.ForumTopicWatches.Any(ft => ft.UserId == userId.Value),
					Title = t.Title,
					ForumId = t.ForumId,
					ForumName = t.Forum.Name,
					IsLocked = t.IsLocked,
					Poll = t.PollId.HasValue
						? new ForumTopicModel.PollModel { PollId = t.PollId.Value, Question = t.Poll.Question }
						: null
				})
				.SingleOrDefaultAsync(t => t.Id == id);

			if (model == null)
			{
				return null;
			}

			var lastPostId = (await _db.ForumPosts
				.Where(p => p.TopicId == id)
				.ByMostRecent()
				.FirstAsync())
				.Id;

			model.Posts = _db.ForumPosts
				.ForTopic(id)
				.Select(p => new ForumTopicModel.ForumPostEntry
				{
					Id = p.Id,
					TopicId = id,
					EnableHtml = p.EnableHtml,
					EnableBbCode = p.EnableBbCode,
					PosterId = p.PosterId,
					CreateTimestamp = p.CreateTimeStamp,
					PosterName = p.Poster.UserName,
					PosterAvatar = p.Poster.Avatar,
					PosterLocation = p.Poster.From,
					PosterRoles = p.Poster.UserRoles
						.Where(ur => !ur.Role.IsDefault)
						.Select(ur => ur.Role.Name)
						.ToList(),
					PosterJoined = p.Poster.CreateTimeStamp,
					PosterPostCount = p.Poster.Posts.Count,
					Text = p.Text,
					Subject = p.Subject,
					Signature = p.Poster.Signature,
					IsLastPost = p.Id == lastPostId
				})
				.OrderBy(p => p.CreateTimestamp)
				.PageOf(_db, paging);

			foreach (var post in model.Posts)
			{
				post.Awards = await _awardTasks.GetAllAwardsForUser(post.PosterId);
			}

			if (model.Poll != null)
			{
				model.Poll.Options = await _db.ForumPollOptions
					.ForPoll(model.Poll.PollId)
					.Select(o => new ForumTopicModel.PollModel.PollOptionModel
					{
						Text = o.Text,
						Ordinal = o.Ordinal,
						Voters = o.Votes
							.Select(v => v.UserId)
							.ToList()
					})
					.ToListAsync();
			}

			return model;
		}

		public async Task<ForumTopic> GetTopic(int id)
		{
			return await _db.ForumTopics
				.Include(t => t.Forum)
				.SingleOrDefaultAsync(t => t.Id == id);
		}

		/// <summary>
		/// Returns the data necessary to create a new forum topic
		/// </summary>
		/// <seealso cref="ForumTopic"/>
		public async Task<TopicCreatePostModel> GetCreateTopicData(int forumId, bool allowRestricted)
		{
			return await _db.Forums
				.ExcludeRestricted(allowRestricted)
				.Where(f => f.Id == forumId)
				.Select(f => new TopicCreatePostModel
				{
					ForumName = f.Name
				})
				.SingleOrDefaultAsync();
		}

		/// <summary>
		/// Creates a new <see cref="ForumTopic" /> and the first <see cref="ForumPost"/> of that topic
		/// </summary>
		/// <returns>
		/// The id of the newly created <see cref="ForumTopic" />
		/// If a topic could not be created, returns null
		/// </returns>
		public async Task<ForumTopic> CreateTopic(int forumId, TopicCreatePostModel model, User user, string ipAddress)
		{
			var topic = new ForumTopic
			{
				Type = model.Type,
				Title = model.Title,
				PosterId = user.Id,
				Poster = user,
				ForumId = forumId
			};

			_db.ForumTopics.Add(topic);
			await _db.SaveChangesAsync();

			var forumPostModel = new ForumPostModel
			{
				Subject = null,
				Text = model.Post
			};

			await CreatePost(topic.Id, forumPostModel, user, ipAddress);
			await WatchTopic(topic.Id, user.Id, canSeeRestricted: true);
			return topic;
		}

		public async Task<int> CreatePost(int topicId, ForumPostModel model, User user, string ipAddress)
		{
			var forumPost = new ForumPost
			{
				TopicId = topicId,
				PosterId = user.Id,
				IpAddress = ipAddress,
				Subject = model.Subject,
				Text = model.Text,

				// TODO: check for bbcode and if none, set this to false?
				// For now we are not giving the user choices
				EnableHtml = false,
				EnableBbCode = true
			};

			_db.ForumPosts.Add(forumPost);
			await _db.SaveChangesAsync();
			await WatchTopic(topicId, user.Id, canSeeRestricted: true);
			return forumPost.Id;
		}

		/// <summary>
		/// Returns necessary data to display on the edit post screen
		/// </summary>
		public async Task<ForumPostEditModel> GetEditPostData(int postId, bool seeRestricted)
		{
			var model = await _db.ForumPosts
				.ExcludeRestricted(seeRestricted)
				.Where(p => p.Id == postId)
				.Select(p => new ForumPostEditModel
				{
					CreateTimestamp = p.CreateTimeStamp,
					PosterId = p.PosterId,
					PosterName = p.Poster.UserName,
					EnableBbCode = p.EnableBbCode,
					EnableHtml = p.EnableHtml,
					TopicId = p.TopicId ?? 0,
					TopicTitle = p.Topic.Title,
					Subject = p.Subject,
					Text = p.Text
				})
				.SingleOrDefaultAsync();

			var lastPostId = (await _db.ForumPosts
				.ForTopic(model.TopicId)
				.ByMostRecent()
				.FirstAsync())
				.Id;

			model.IsLastPost = postId == lastPostId;

			return model;
		}

		/// <summary>
		/// Returns whether or not the poster matches the given user
		/// and that the post is currently the last post of its topic
		/// </summary>
		public async Task<bool> CanEdit(int postId, int userId)
		{
			using (await _db.Database.BeginTransactionAsync())
			{
				var post = await _db.ForumPosts.SingleOrDefaultAsync(p => p.Id == postId);

				if (post == null || post.PosterId != userId)
				{
					return false;
				}

				var lastPostId = await _db.ForumPosts
					.ForTopic(post.TopicId ?? 0)
					.ByMostRecent()
					.Select(p => p.Id)
					.FirstAsync();

				return post.Id == lastPostId;
			}
		}

		/// <summary>
		/// Updates the post with the given post id, with the
		/// given subject and text
		/// </summary>
		public async Task EditPost(int id, ForumPostEditModel model)
		{
			var forumPost = await _db.ForumPosts.SingleAsync(p => p.Id == id);
			forumPost.Subject = model.Subject;
			forumPost.Text = model.Text;
			await _db.SaveChangesAsync();
		}

		/// <summary>
		/// Results the vote results of a given <see cref="ForumPoll"/> including
		/// the voters and which options they voted for
		/// </summary>
		public async Task<PollResultModel> GetPollResults(int pollId)
		{
			var poll = await _db.ForumPolls
				.Include(p => p.Topic)
				.Include(p => p.PollOptions)
				.ThenInclude(po => po.Votes)
				.ThenInclude(v => v.User)
				.SingleOrDefaultAsync(p => p.Id == pollId);

			if (poll == null)
			{
				return null;
			}

			return new PollResultModel
			{
				TopicTitle = poll.Topic.Title,
				TopicId = poll.TopicId,
				PollId = pollId,
				Question = poll.Question,
				Votes = poll.PollOptions
					.SelectMany(p => p.Votes)
					.Select(v => new PollResultModel.VoteResult
					{
						UserId = v.UserId,
						UserName = v.User.UserName,
						Ordinal = v.PollOption.Ordinal,
						OptionText = v.PollOption.Text,
						CreateTimestamp = v.CreateTimestamp,
						IpAddress = v.IpAddress
					})
			};
		}

		public async Task<SplitTopicModel> GetTopicForSplit(int topicId, bool allowRestricted)
		{
			var model = await _db.ForumTopics
				.ExcludeRestricted(allowRestricted)
				.Where(t => t.Id == topicId)
				.Select(t => new SplitTopicModel
				{
					Title = t.Title,
					SplitTopicName = "(Split from " + t.Title + ")",
					SplitToForumId = t.Forum.Id,
					ForumId = t.Forum.Id,
					ForumName = t.Forum.Name,
					Posts = t.ForumPosts
						.Select(p => new SplitTopicModel.Post
						{
							Id = p.Id,
							PostCreateTimeStamp = p.CreateTimeStamp,
							EnableBbCode = p.EnableBbCode,
							EnableHtml = p.EnableHtml,
							Subject = p.Subject,
							Text = p.Text,
							PosterId = p.PosterId,
							PosterName = p.Poster.UserName,
							PosterAvatar = p.Poster.Avatar
						})
						.ToList()
				})
				.SingleOrDefaultAsync();

			if (model != null)
			{
				model.AvailableForums = await _db.Forums
					.ExcludeRestricted(allowRestricted)
					.Select(f => new SelectListItem
					{
						Text = f.Name,
						Value = f.Id.ToString(),
						Selected = f.Id == model.ForumId
					})
					.ToListAsync();
			}

			return model;
		}

		/// <summary>
		/// Splits the given topic starting at the given starting post into a new topic
		/// with the given values
		/// </summary>
		/// <returns>Returns new topic id, if old topic, split post, and new forum are found, else null</returns>
		public async Task<int?> SplitTopic(int id, SplitTopicModel model, bool allowRestricted, User user)
		{
			var topic = await _db.ForumTopics
				.Include(t => t.Forum)
				.Include(t => t.ForumPosts)
				.ExcludeRestricted(allowRestricted)
				.SingleOrDefaultAsync(t => t.Id == id);

			if (topic == null)
			{
				return null;
			}

			var destinationForum = _db.Forums
				.ExcludeRestricted(allowRestricted)
				.SingleOrDefaultAsync(f => f.Id == model.SplitToForumId);

			if (destinationForum == null)
			{
				return null;
			}

			var splitOnPost = topic.ForumPosts
				.SingleOrDefault(p => p.Id == model.PostToSplitId);

			if (splitOnPost == null)
			{
				return null;
			}

			var newTopic = new ForumTopic
			{
				Type = ForumTopicType.Regular,
				Title = model.SplitTopicName,
				PosterId = user.Id,
				Poster = user,
				ForumId = model.SplitToForumId
			};

			_db.ForumTopics.Add(newTopic);
			await _db.SaveChangesAsync();

			var splitPosts = topic.ForumPosts
				.Where(p => p.Id == splitOnPost.Id
					|| p.CreateTimeStamp > splitOnPost.CreateTimeStamp);

			foreach (var post in splitPosts)
			{
				post.TopicId = newTopic.Id;
			}

			await _db.SaveChangesAsync();
			return newTopic.Id;
		}

		/// <summary>
		/// Deletes a post with the given <see cref="postId"/>
		/// </summary>
		/// <returns>the topic id that contained the post if post is successfully deleted, if user can not delete the post or a post of the given id is not found then null</returns>
		public async Task<int?> DeletePost(int postId, bool canDelete, bool canSeeRestricted)
		{
			var post = await _db.ForumPosts
				// TODO: add includes?
				.ExcludeRestricted(canSeeRestricted)
				.SingleOrDefaultAsync(p => p.Id == postId);

			if (post == null)
			{
				return null;
			}

			if (!canDelete)
			{
				// Check if last post
				var lastPost = _db.ForumPosts
					.ForTopic(post.TopicId ?? -1)
					.ByMostRecent()
					.First();

				bool isLastPost = lastPost.Id == post.Id;
				if (!isLastPost)
				{
					return null;
				}
			}

			_db.ForumPosts.Remove(post);
			await _db.SaveChangesAsync();

			return post.TopicId;
		}

		public async Task<UserPostsModel> PostsByUser(string userName, UserPostsRequest paging, bool allowRestricted)
		{
			var model = await _db.Users
				.Where(u => u.UserName == userName)
				.Select(u => new UserPostsModel
				{
					Id = u.Id,
					UserName = u.UserName,
					Joined = u.CreateTimeStamp,
					Location = u.From,
					Avatar = u.Avatar,
					Signature = u.Signature,
					Roles = u.UserRoles
						.Where(ur => !ur.Role.IsDefault)
						.Select(ur => ur.Role.Name)
						.ToList()
				})
				.SingleOrDefaultAsync();

			if (model == null)
			{
				return null;
			}

			model.Awards = await _awardTasks.GetAllAwardsForUser(model.Id);

			model.Posts = _db.ForumPosts
				.CreatedBy(model.UserName)
				.ExcludeRestricted(allowRestricted)
				.Select(p => new UserPostsModel.Post
				{
					Id = p.Id,
					CreateTimestamp = p.CreateTimeStamp,
					EnableHtml = p.EnableHtml,
					EnableBbCode = p.EnableBbCode,
					Text = p.Text,
					Subject = p.Subject,
					TopicId = p.TopicId ?? 0,
					TopicTitle = p.Topic.Title,
					ForumId = p.Topic.ForumId,
					ForumName = p.Topic.Forum.Name
				})
				.SortedPageOf(_db, paging);

			return model;
		}

		/// <summary>
		/// If a user is watching this topic, this marks the topic
		/// as not notified, at which point, any new post will cause a notification
		/// </summary>
		public async Task MarkTopicAsUnNotifiedForUser(int userId, int topicId)
		{
			var watchedTopic = await _db.ForumTopicWatches
				.SingleOrDefaultAsync(w => w.UserId == userId && w.ForumTopicId == topicId);

			if (watchedTopic != null && watchedTopic.IsNotified)
			{
				watchedTopic.IsNotified = false;
				await _db.SaveChangesAsync();
			}
		}

		/// <summary>
		/// Should be called when a new post is created in a topic
		/// Will notify all users watching the topic and mark the IsNotified flag accordingly
		/// </summary>
		public async Task NotifyWatchedTopics(int topicId, int posterId)
		{
			var watches = await _db.ForumTopicWatches
				.Include(w => w.User)
				.Where(w => w.ForumTopicId == topicId)
				.Where(w => w.UserId != posterId)
				.Where(w => !w.IsNotified)
				.ToListAsync();

			if (watches.Any())
			{
				await _emailService.SendTopicNotification(watches.Select(w => w.User.Email));

				foreach (var watch in watches)
				{
					watch.IsNotified = true;
				}

				await _db.SaveChangesAsync();
			}
		}

		public async Task WatchTopic(int topicId, int userId, bool canSeeRestricted)
		{
			var watch = await _db.ForumTopicWatches
				.ExcludeRestricted(canSeeRestricted)
				.SingleOrDefaultAsync(w => w.UserId == userId
				&& w.ForumTopicId == topicId);

			if (watch == null)
			{
				_db.ForumTopicWatches.Add(new ForumTopicWatch
				{
					UserId = userId,
					ForumTopicId = topicId
				});

				await _db.SaveChangesAsync();
			}
		}

		public async Task UnwatchTopic(int topicId, int userId, bool canSeeRestricted)
		{
			var watch = await _db.ForumTopicWatches
				.ExcludeRestricted(canSeeRestricted)
				.SingleOrDefaultAsync(w => w.UserId == userId
				&& w.ForumTopicId == topicId);

			if (watch != null)
			{
				_db.ForumTopicWatches.Remove(watch);
				await _db.SaveChangesAsync();
			}
		}
	}
}
