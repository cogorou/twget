/*
	Copyright (C) 2017 Eggs Imaging Laboratory
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Management;
using System.Configuration;
using System.Text.RegularExpressions;

namespace twget
{
	static class Program
	{
		/// <summary>
		/// エントリポイント
		/// </summary>
		/// <param name="args"></param>
		[STAThread]
		static void Main(string[] args)
		{
			try
			{
				#region 主処理:
				do
				{
					var commands = ParseArguments(args);

					string value = "";

					// ヘルプ:
					if (commands.TryGetValue("?", out value))
					{
						Usage();
						break;
					}

					// バージョン:
					if (commands.TryGetValue("v", out value))
					{
						Version();
						break;
					}
					
					// トレンドキーワードリスト:
					if (commands.TryGetValue("trends", out value))
					{
						var tokens = GetTokens();
						Trends(tokens);
						break;
					}
					
					// フォロー中のユーザーリスト:
					if (commands.TryGetValue("friends", out value))
					{
						var tokens = GetTokens();
						Friends(tokens);
						break;
					}

					// 本日のタイムライン:
					if (commands.TryGetValue("today", out value))
					{
						var offset_days = (string.IsNullOrWhiteSpace(value)) ? 0 : Convert.ToInt32(value);

						var tokens = GetTokens();
						Today(tokens, offset_days);
						break;
					}

					// ホームタイムライン:
					if (commands.TryGetValue("home", out value))
					{
						var count = (string.IsNullOrWhiteSpace(value)) ? 0 : Convert.ToInt32(value);

						var tokens = GetTokens();
						HomeTimeline(tokens, count);
						break;
					}

					// ユーザータイムライン:
					if (commands.TryGetValue("user", out value))
					{
						var count = (string.IsNullOrWhiteSpace(value)) ? 0 : Convert.ToInt32(value);

						var screen_name = "";
						if (Parameters.Count > 0)
							screen_name = Parameters[0];
						screen_name = screen_name.Trim();
						if (string.IsNullOrWhiteSpace(screen_name))
						{
							Console.WriteLine("ERR: Screen Name is not specified.");
							break;
						}

						var tokens = GetTokens();
						UserTimeline(tokens, screen_name, count);
						break;
					}

					// 検索:
					if (commands.TryGetValue("search", out value))
					{
						var count = (string.IsNullOrWhiteSpace(value)) ? 0 : Convert.ToInt32(value);

						var keyword = "";
						foreach (var item in Parameters)
							keyword = keyword + " " + item;
						keyword = keyword.Trim();
						if (string.IsNullOrWhiteSpace(keyword))
						{
							Console.WriteLine("ERR: No keywords are specified.");
							break;
						}

						var tokens = GetTokens();
						Search(tokens, keyword, count);
						break;
					}

					// エラー:
					{
						Console.WriteLine("ERR: Invalid argument.");
						Usage();
					}
				} while (false);
				#endregion
			}
			catch (System.Exception ex)
			{
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
			}
		}

		#region 引数解析:

		/// <summary>
		/// パラメータ
		/// </summary>
		static List<string> Parameters = new List<string>();

		/// <summary>
		/// 引数解析
		/// </summary>
		/// <param name="args">コマンドライン引数</param>
		/// <returns>
		///		解析結果を返します。
		/// </returns>
		private static Dictionary<string, string> ParseArguments(string[] args)
		{
			var commands = new Dictionary<string, string>();

			foreach (string arg in args)
			{
				if (arg.StartsWith("/"))
				{
					int colon = arg.IndexOf(':');
					if (colon > 1)
					{
						string key = arg.Substring(1, colon - 1);
						string val = arg.Substring(colon + 1);
						commands[key] = val;
					}
					else
					{
						string key = arg.Substring(1);
						commands[key] = "";
					}
				}
				else
				{
					Parameters.Add(arg);
				}
			}

			return commands;
		}

		#endregion

		#region ファイル関連:

		/// <summary>
		/// ファイル名のサフィックス(年月日_時分秒)の作成
		/// </summary>
		/// <param name="timestamp">ファイル名のサフィックスにするタイムスタンプ</param>
		/// <param name="use_msec">ミリ秒も付加するか否か</param>
		/// <returns>
		///		生成したサフィックスを返します。
		/// </returns>
		public static string MakeFileNameSuffix(DateTime timestamp, bool use_msec)
		{
			string suffix = string.Format("{0:0000}{1:00}{2:00}-{3:00}{4:00}{5:00}",
				timestamp.Year, timestamp.Month, timestamp.Day,
				timestamp.Hour, timestamp.Minute, timestamp.Second);
			if (use_msec)
				suffix += "-" + timestamp.Millisecond.ToString("000");
			return suffix;
		}

		#endregion

		#region 認証関連:

		/// <summary>
		/// プロジェクトディレクトリ
		/// </summary>
		static string ProjectDir = "";

		/// <summary>
		/// 認証
		/// </summary>
		/// <returns>
		///		トークンを返します。
		/// </returns>
		static CoreTweet.Tokens GetTokens()
		{
			CoreTweet.Tokens tokens = null;
			const string tokens_fileName = "Tokens.xml";

			#region 構成ファイルの読み込み: (ConsumerKey/ConsumerSecret)
			string consumerKey = "";
			string consumerSecret = "";
			if (ConfigurationManager.AppSettings["ConsumerKey"] is string)
				consumerKey = (string)ConfigurationManager.AppSettings["ConsumerKey"];
			if (ConfigurationManager.AppSettings["ConsumerSecret"] is string)
				consumerSecret = (string)ConfigurationManager.AppSettings["ConsumerSecret"];
			#endregion

			#region 構成ファイルの読み込み: (ProjectDir)
			string projectDir = "";
			if (ConfigurationManager.AppSettings["ProjectDir"] is string)
				projectDir = (string)ConfigurationManager.AppSettings["ProjectDir"];

			// 構成ファイルから読み込んだ情報の編集.
			if (string.IsNullOrWhiteSpace(projectDir) == false)
			{
				#region 特殊フォルダ.
				System.Environment.SpecialFolder[] folders =
				{
					System.Environment.SpecialFolder.MyDocuments,
					System.Environment.SpecialFolder.MyMusic,
					System.Environment.SpecialFolder.MyPictures,
					System.Environment.SpecialFolder.MyVideos,
					System.Environment.SpecialFolder.MyComputer,
				};

				var asm = System.Reflection.Assembly.GetExecutingAssembly();
				// アプリケーション情報.
				var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
				var app_name = System.IO.Path.GetFileNameWithoutExtension(asm.Location);

				foreach (var folder in folders)
				{
					string label = string.Format("({0})", folder);
					if (string.Compare(projectDir, label, true) == 0)
					{
						string base_dir = System.Environment.GetFolderPath(folder);
						string home_dir = string.Format("{0}-{1}.{2}", app_name, fvi.FileMajorPart, fvi.FileMinorPart);
						projectDir = System.IO.Path.Combine(base_dir, home_dir);
						if (System.IO.Directory.Exists(projectDir) == false)
							System.IO.Directory.CreateDirectory(projectDir);
						break;
					}
				}
				#endregion
			}
			#endregion

			#region プロジェクトディレクトリの設定:
			if (string.IsNullOrWhiteSpace(projectDir) == true ||
				System.IO.Directory.Exists(projectDir) == false)
			{
				ProjectDir = System.IO.Directory.GetCurrentDirectory();
			}
			else
			{
				ProjectDir = System.IO.Path.GetFullPath(projectDir);
			}
			#endregion

			#region 前回の認証情報が有ればトークンを復元します.
			var token_filepath = System.IO.Path.Combine(ProjectDir, tokens_fileName);
			if (System.IO.File.Exists(token_filepath))
			{
				try
				{
					var param = (CoreTweet.Tokens)ReadAsXml(token_filepath, typeof(CoreTweet.Tokens));

					tokens = CoreTweet.Tokens.Create(
						param.ConsumerKey,
						param.ConsumerSecret,
						param.AccessToken,
						param.AccessTokenSecret,
						param.UserId,
						param.ScreenName
						);
				}
				catch (System.Exception ex)
				{
					Console.WriteLine(ex.Message);
				}
			}
			#endregion

			#region トークンが無ければ認証を行います.
			if (tokens == null)
			{
				// アプリケーションを認証します.
				var session = CoreTweet.OAuth.Authorize(consumerKey, consumerSecret);

				#region 確認用:
#if DEBUG
				{
					Console.WriteLine("{0,-20}: {1}", "ConsumerKey", session.ConsumerKey);
					Console.WriteLine("{0,-20}: {1}", "ConsumerSecret", session.ConsumerSecret);
					Console.WriteLine("{0,-20}: {1}", "RequestToken", session.RequestToken);
					Console.WriteLine("{0,-20}: {1}", "RequestTokenSecret", session.RequestTokenSecret);
					Console.WriteLine("{0,-20}: {1}", "AuthorizeUri", session.AuthorizeUri);
					Console.WriteLine("{0,-20}:", "ConnectionOptions");
					Console.WriteLine("- {0,-18}: {1}", "ApiUrl", session.ConnectionOptions.ApiUrl);
					Console.WriteLine("- {0,-18}: {1}", "UploadUrl", session.ConnectionOptions.UploadUrl);
					Console.WriteLine("- {0,-18}: {1}", "UserStreamUrl", session.ConnectionOptions.UserStreamUrl);
					Console.WriteLine("- {0,-18}: {1}", "SiteStreamUrl", session.ConnectionOptions.SiteStreamUrl);
					Console.WriteLine("- {0,-18}: {1}", "StreamUrl", session.ConnectionOptions.StreamUrl);
					Console.WriteLine("- {0,-18}: {1}", "ApiVersion", session.ConnectionOptions.ApiVersion);
					Console.WriteLine("- {0,-18}: {1}", "Timeout", session.ConnectionOptions.Timeout);
					Console.WriteLine("- {0,-18}: {1}", "ReadWriteTimeout", session.ConnectionOptions.ReadWriteTimeout);
					Console.WriteLine("- {0,-18}: {1}", "UseProxy", session.ConnectionOptions.UseProxy);
					Console.WriteLine("- {0,-18}: {1}", "UserAgent", session.ConnectionOptions.UserAgent);
					Console.WriteLine("- {0,-18}: {1}", "UseCompression", session.ConnectionOptions.UseCompression);
					Console.WriteLine("- {0,-18}: {1}", "UseCompressionOnStreaming", session.ConnectionOptions.UseCompressionOnStreaming);
					Console.WriteLine("- {0,-18}: {1}", "DisableKeepAlive", session.ConnectionOptions.DisableKeepAlive);
				}
#endif
				#endregion

				// アクセス トークン/シークレット を取得します.
				System.Diagnostics.Process.Start(session.AuthorizeUri.AbsoluteUri);

				// コンソールから PIN を取得します.
				Console.Write("Input PIN code. _ ");
				var pin = Console.ReadLine();

				#region 確認用:
#if DEBUG
				{
					// Input PIN code. _ 8862891
					// PIN code            : 8862891
					Console.WriteLine("{0,-20}: {1}", "PIN code", pin);
				}
#endif
				#endregion

				// トークンを取得します.
				tokens = CoreTweet.OAuth.GetTokens(session, pin);

				// シリアライズ:
				WriteAsXml(token_filepath, tokens);
			}
			#endregion

			return tokens;
		}

		#endregion

		#region XMLシリアライズ関連:

		/// <summary>
		/// XML ファイルからの読み込み
		/// </summary>
		/// <param name="filename">ファイル名</param>
		/// <param name="type">復元するクラスの型</param>
		/// <returns>
		///		復元したオブジェクトを返します。
		/// </returns>
		public static object ReadAsXml(string filename, Type type)
		{
			var serializer = new System.Runtime.Serialization.DataContractSerializer(type);
			using (var xr = System.Xml.XmlReader.Create(filename))
			{
				var result = serializer.ReadObject(xr);
				return result;
			}
		}

		/// <summary>
		/// XML ファイルへの書き込み
		/// </summary>
		/// <param name="filename">ファイル名</param>
		/// <param name="target">書き込み対象</param>
		public static void WriteAsXml(string filename, object target)
		{
			var serializer = new System.Runtime.Serialization.DataContractSerializer(target.GetType());
			var settings = new System.Xml.XmlWriterSettings();
			settings.Encoding = new System.Text.UTF8Encoding(false);
			using (var xw = System.Xml.XmlWriter.Create(filename, settings))
			{
				serializer.WriteObject(xw, target);
			}
		}

		#endregion

		#region テキスト出力関数:

		/// <summary>
		/// Photo
		/// </summary>
		/// <param name="stream">出力先のストリーム</param>
		/// <param name="entities">エンティティ</param>
		static void WritePhoto(this System.IO.StreamWriter stream, CoreTweet.Entities entities)
		{
			const int image_max_size = 480;

			if (entities != null)
			{
				if (entities.Media != null)
				{
					foreach (var entity in entities.Media)
					{
						string media_url = "";
						if (string.IsNullOrWhiteSpace(entity.MediaUrlHttps) == false)
							media_url = entity.MediaUrlHttps;
						else if (string.IsNullOrWhiteSpace(entity.MediaUrl) == false)
							media_url = entity.MediaUrl;
						if (string.IsNullOrWhiteSpace(media_url) == false)
						{
							double mag_x = (double)entity.Sizes.Small.Width / image_max_size;
							double mag_y = (double)entity.Sizes.Small.Height / image_max_size;
							double mag = System.Math.Max(mag_x, mag_y);
							if (mag < 1)
								mag = 1;
							int width = (int)System.Math.Round(entity.Sizes.Small.Width / mag);
							int height = (int)System.Math.Round(entity.Sizes.Small.Height / mag);

							stream.WriteLine(
								"<a href=\"{0}\" target=_blank><img src=\"{0}\" width={1} height={2}></a>",
								media_url, width, height
								);
						}
					}
				}
			}
		}

		/// <summary>
		/// Entities
		/// </summary>
		/// <param name="stream">出力先のストリーム</param>
		/// <param name="entities">エンティティ</param>
		static void WriteEntities(this System.IO.StreamWriter stream, CoreTweet.Entities entities)
		{
			if (entities != null)
			{
				if (entities.Urls != null)
				{
					stream.WriteLine("- Urls:");
					foreach (var entity in entities.Urls)
					{
						stream.WriteLine("	- Url        : <a href=\"{0}\" target=_blank>{0}</a>", entity.Url);
						stream.WriteLine("	- ExpandedUrl: <a href=\"{0}\" target=_blank>{0}</a>", entity.ExpandedUrl);
						stream.WriteLine("	- DisplayUrl : {0}", entity.DisplayUrl);
					}
				}
				if (entities.Media != null)
				{
					stream.WriteLine("- Media:");
					foreach (var entity in entities.Media)
					{
						stream.WriteLine("	- Id: {0}", entity.Id);
						stream.WriteLine("	- Type       : {0}", entity.Type);
						stream.WriteLine("	- Url        : <a href=\"{0}\" target=_blank>{0}</a>", entity.Url);
						stream.WriteLine("	- ExpandedUrl: <a href=\"{0}\" target=_blank>{0}</a>", entity.ExpandedUrl);
						stream.WriteLine("	- DisplayUrl : {0}", entity.DisplayUrl);
						stream.WriteLine("	- ExtAltText : {0}", entity.ExtAltText);
						stream.WriteLine("	- MediaUrl     : <a href=\"{0}\" target=_blank>{0}</a>", entity.MediaUrl);
						stream.WriteLine("	- MediaUrlHttps: <a href=\"{0}\" target=_blank>{0}</a>", entity.MediaUrlHttps);
						stream.WriteLine("	- Sizes:");
						stream.WriteLine("		- L: {0},{1}", entity.Sizes.Large.Width, entity.Sizes.Large.Height);
						stream.WriteLine("		- M: {0},{1}", entity.Sizes.Medium.Width, entity.Sizes.Medium.Height);
						stream.WriteLine("		- S: {0},{1}", entity.Sizes.Small.Width, entity.Sizes.Small.Height);
						stream.WriteLine("		- T: {0},{1}", entity.Sizes.Thumb.Width, entity.Sizes.Thumb.Height);
						stream.WriteLine("");
					}
				}
				if (entities.HashTags != null)
				{
					stream.WriteLine("- HashTags:");
					foreach (var entity in entities.HashTags)
					{
						stream.WriteLine("	- Text: {0}", entity.Text);
					}
				}
				if (entities.Symbols != null)
				{
					stream.WriteLine("- Symbols:");
					foreach (var entity in entities.Symbols)
					{
						stream.WriteLine("	- {0}", entity.Text);
					}
				}
				if (entities.UserMentions != null)
				{
					stream.WriteLine("- UserMentions:");
					foreach (var entity in entities.UserMentions)
					{
						stream.WriteLine("	- Name: {0} @{1}", entity.Name, entity.ScreenName);
					}
				}
			}
		}

		/// <summary>
		/// URL や ハッシュタグの置き換え
		/// </summary>
		/// <param name="src">変換元の文字列</param>
		/// <param name="entities">エンティティ</param>
		static string ReplaceTag(this string src, CoreTweet.Entities entities)
		{
			string result = src.Trim();
			if (entities != null)
			{
				if (entities.Urls != null)
				{
					foreach (var entity in entities.Urls)
					{
						result = result.Replace(entity.Url, string.Format("<a href=\"{0}\" target=_blank>{0}</a>", entity.Url));
					}
				}
				if (entities.Media != null)
				{
					foreach (var entity in entities.Media)
					{
						result = result.Replace(entity.Url, "");
					}
				}
				if (entities.HashTags != null)
				{
					foreach (var entity in entities.HashTags)
					{
						result = result.Replace(
							string.Format("#{0}", entity.Text),
							string.Format("<font color=\"#0000FF\">{0}</font>", entity.Text)
							);
					}
				}
				if (entities.Symbols != null)
				{
				}
				if (entities.UserMentions != null)
				{
					foreach (var entity in entities.UserMentions)
					{
						// RT @～:
						if (new Regex("(?<=RT @).*?(?=:)", RegexOptions.IgnoreCase).Match(result).Value == entity.ScreenName)
						{
							result = result.Replace(
								string.Format("RT @{0}:", entity.ScreenName),
								string.Format("RT: <a href=\"https://twitter.com/{1}\" target=_blank>{0} @{1}</a><br/>", entity.Name, entity.ScreenName)
								);
						}
						// @～
						else
						{
							result = result.Replace(
								string.Format("@{0}", entity.ScreenName),
								string.Format("返信先: <a href=\"https://twitter.com/{1}\" target=_blank>{0} @{1}</a><br/>", entity.Name, entity.ScreenName)
								);
						}
					}
				}
			}
			return result;
		}

		#endregion

		#region コマンド: (使用方法の表示)

		/// <summary>
		/// 使用方法の表示
		/// </summary>
		public static void Usage()
		{
			string[] lines = 
			{
				"",
				"Usage:",
				"   twget.exe [command]",
				"   twget.exe [switch] [arguments]",
				"",
				"Command)",
				"   /? ... show help.",
				"   /v ... show version.",
				"",
				"Trends list",
				"   twget.exe /trends",
				"",
				"Friends list",
				"   twget.exe /friends",
				"",
				"Today Timeline",
				"   twget.exe /today:[offset_days]",
				"   params)",
				"   - offset_days = 0~",
				"   ex)",
				"   > twget.exe /today",
				"   > twget.exe /today:7",
				"",
				"Home Timeline",
				"   twget.exe /home:[offset_days]",
				"   params)",
				"   - offset_days = 0~",
				"     (!) There seems to be a bug. Only 2 days can be taken.",
				"         Also, It reaches the rate limit with 10 requests.",
				"   ex)",
				"   > twget.exe /home",
				"   > twget.exe /home:2",
				"",
				"User Timeline",
				"   twget.exe /user:[offset_days] <screen_name>",
				"   params)",
				"   - offset_days = 0~",
				"   - screen_name = user screen name",
				"   ex)",
				"   > twget.exe /user maruko",
				"   > twget.exe /user:7 taro",
				"",
				"Search",
				"   twget.exe /search:[offset_days] <keywords>",
				"   params)",
				"   - offset_days = 0~7",
				"   - keywords = one or more search keywords",
				"   ex)",
				"   > twget.exe /search NOKTON",
				"   > twget.exe /search:7 #camera sigma OR nikon",
				"",
				"",
			};
			foreach (string line in lines)
			{
				Console.WriteLine(line);
			}
		}

		#endregion

		#region コマンド: (バージョン表示)

		/// <summary>
		/// バージョン表示
		/// </summary>
		public static void Version()
		{
			try
			{
				#region アプリケーションのバージョン:
				{
					Console.WriteLine("[Application]");
					var asm = Assembly.GetExecutingAssembly();
					var info = FileVersionInfo.GetVersionInfo(asm.Location);
					Console.WriteLine("{0,-20}: {1}", "FullName", asm.FullName);
					Console.WriteLine("{0,-20}: {1}", "FileName", System.IO.Path.GetFileName(info.FileName));
					Console.WriteLine("{0,-20}: {1}", "FileDescription", info.FileDescription);
					Console.WriteLine("{0,-20}: {1}", "Comments", info.Comments);
					Console.WriteLine("{0,-20}: {1}", "FileVersion", info.FileVersion);
					Console.WriteLine("{0,-20}: {1}", "ProductVersion", info.ProductVersion);
					Console.WriteLine("{0,-20}: {1}", "ProductName", info.ProductName);
					Console.WriteLine("{0,-20}: {1}", "CompanyName", info.CompanyName);
					Console.WriteLine("{0,-20}: {1}", "LegalCopyright", info.LegalCopyright);
					Console.WriteLine("{0,-20}: {1}", "LegalTrademarks", info.LegalTrademarks);
					Console.WriteLine("{0,-20}: {1}", "Language", info.Language);
					//Console.WriteLine("{0,-20}: {1}", "InternalName", info.InternalName);
					//Console.WriteLine("{0,-20}: {1}", "OriginalFilename", info.OriginalFilename);
					//Console.WriteLine("{0,-20}: {1}", "IsDebug", info.IsDebug);
					//Console.WriteLine("{0,-20}: {1}", "IsPatched", info.IsPatched);
					//Console.WriteLine("{0,-20}: {1}", "IsPreRelease", info.IsPreRelease);
					//Console.WriteLine("{0,-20}: {1}", "IsPrivateBuild", info.IsPrivateBuild);
					//Console.WriteLine("{0,-20}: {1}", "IsSpecialBuild", info.IsSpecialBuild);
					//Console.WriteLine("{0,-20}: {1}", "PrivateBuild", info.PrivateBuild);
					//Console.WriteLine("{0,-20}: {1}", "SpecialBuild", info.SpecialBuild);
					//Console.WriteLine("{0,-20}: {1}", "FileBuildPart", info.FileBuildPart);
					//Console.WriteLine("{0,-20}: {1}", "FileMajorPart", info.FileMajorPart);
					//Console.WriteLine("{0,-20}: {1}", "FileMinorPart", info.FileMinorPart);
					//Console.WriteLine("{0,-20}: {1}", "FilePrivatePart", info.FilePrivatePart);
					//Console.WriteLine("{0,-20}: {1}", "ProductBuildPart", info.ProductBuildPart);
					//Console.WriteLine("{0,-20}: {1}", "ProductMajorPart", info.ProductMajorPart);
					//Console.WriteLine("{0,-20}: {1}", "ProductMinorPart", info.ProductMinorPart);
					//Console.WriteLine("{0,-20}: {1}", "ProductPrivatePart", info.ProductPrivatePart);
				}
				#endregion

				#region O/S、CPU、バージョン情報:
				{
					Console.WriteLine("");
					Console.WriteLine("[Environment]");
					Console.WriteLine("{0,-20}: {1} ({2})", ".NET Framework", Environment.Version, (Environment.Is64BitOperatingSystem ? "x64" : "x86"));
					Console.WriteLine("{0,-20}: {1}", "OS", Environment.OSVersion);
					try
					{
						string[] keys = 
							{
								"Name",
								"Description",
								"CurrentClockSpeed",
								"MaxClockSpeed",
								"NumberOfCores",
								"NumberOfLogicalProcessors",
							};
						var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
						var moc = searcher.Get();
						foreach (var obj in moc)
						{
							foreach (string key in keys)
							{
								Console.WriteLine("{0,-20}: {1}", key, obj[key]);
							}
						}
					}
					catch (System.Exception)
					{
					}
				}
				#endregion
			}
			catch (System.Exception ex)
			{
				Console.WriteLine(ex.Message);
			}
		}

		#endregion

		#region コマンド: (トレンド)

		/// <summary>
		/// トレンド
		/// </summary>
		/// <param name="tokens">トークン</param>
		static void Trends(CoreTweet.Tokens tokens)
		{
			var __FUNCTION__ = MethodBase.GetCurrentMethod().Name;

			// Rate Limit Status
			{
				var rls = tokens.Application.RateLimitStatus().RateLimit;
				Console.WriteLine(
					"Limit:{0} Remaining={1} Reset={2}",
					rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
					);
			}

			foreach (var location in tokens.Trends.Available())
			{
				if (location.Country.ToLower() == "japan")
				{
					//Console.WriteLine("Country    : {0} ({1})", location.Country, location.CountryCode);
					//Console.WriteLine("Name       : {0}", location.Name);
					//Console.WriteLine("ParentId   : {0}", location.ParentId);
					//Console.WriteLine("WoeId      : {0}", location.WoeId);
					//Console.WriteLine("PlaceType  : {0} ({1})", location.PlaceType.Name, location.PlaceType.Code);
					//Console.WriteLine("Url        : {0}", location.Url);
					//Console.WriteLine("");

					// Japan		23424856
					// Kitakyushu	1110809
					// Saitama		1116753
					// Chiba		1117034
					// Fukuoka		1117099
					// Hamamatsu	1117155
					// Hiroshima	1117227
					// Kawasaki		1117502
					// Kobe			1117545
					// Kumamoto		1117605
					// Nagoya		1117817
					// Niigata		1117881
					// Sagamihara	1118072
					// Sapporo		1118108
					// Sendai		1118129
					// Takamatsu	1118285
					// Tokyo		1118370
					// Yokohama		1118550
					// Okinawa		2345896
					// Osaka		15015370
					// Kyoto		15015372
					// Okayama		90036018

					var is_japan = (location.ParentId == 1 && location.WoeId == 23424856);
					//var is_yokohama = (location.ParentId == 23424856 && location.WoeId == 1118550);

					if (is_japan)
					{
						// 収集:
						var trends = new List<CoreTweet.Trend>();
						foreach (var result in tokens.Trends.Place(location.WoeId))
						{
							foreach (var trend in result.Trends)
							{
								trends.Add(trend);
							}
						}

						// 出力:
						var now = DateTime.Now;
						var suffix = MakeFileNameSuffix(now, true);
						var filename = string.Format("{0}-{1}.md", __FUNCTION__, suffix);
						using (var stream = new StreamWriter(filename, false, System.Text.Encoding.UTF8))
						{
							stream.WriteLine(__FUNCTION__);
							stream.WriteLine("====");

							stream.WriteLine("");
							stream.WriteLine("{0}  ", now);

							stream.WriteLine("");
							stream.WriteLine("|Item|Value|  ");
							stream.WriteLine("|---|---|  ");
							stream.WriteLine("|Country|{0} ({1})|  ", location.Country, location.CountryCode);
							stream.WriteLine("|Name|{0}|  ", location.Name);
							stream.WriteLine("|ParentId|{0}|  ", location.ParentId);
							stream.WriteLine("|WoeId|{0}|  ", location.WoeId);
							stream.WriteLine("|PlaceType|{0} ({1})|  ", location.PlaceType.Name, location.PlaceType.Code);
							stream.WriteLine("|Url|{0}|  ", location.Url);
							stream.WriteLine("");

							stream.WriteLine("");
							stream.WriteLine("List:  ");
							stream.WriteLine("");

							// 並び替え:
							trends.Sort((ope1, ope2) =>
								{
									if (ope1.TweetVolume == null && ope2.TweetVolume == null)
									{
										return StringComparer.CurrentCulture.Compare(ope1.Name, ope2.Name);
									}
									if (ope1.TweetVolume == null && ope2.TweetVolume != null) return +1;
									if (ope1.TweetVolume != null && ope2.TweetVolume == null) return -1;
									int ope1_val = (int)ope1.TweetVolume;
									int ope2_val = (int)ope2.TweetVolume;
									return ope1_val.CompareTo(ope2_val);
								});
							// リスト化:
							foreach (var trend in trends)
							{
								stream.WriteLine("- <a href=\"{0}\">{1}</a> ({2})  ", trend.Url, trend.Name, trend.TweetVolume);
							}
							stream.WriteLine("");
						}
					}
				}
			}
		}

		#endregion

		#region コマンド: (フォロー中のユーザー)

		/// <summary>
		/// フォロー中のユーザー
		/// </summary>
		/// <param name="tokens">トークン</param>
		static void Friends(CoreTweet.Tokens tokens)
		{
			var __FUNCTION__ = MethodBase.GetCurrentMethod().Name;
			var suffix = MakeFileNameSuffix(DateTime.Now, true);
			var filename = string.Format("{0}-{1}.md", __FUNCTION__, suffix);
			using (var stream = new StreamWriter(filename, false, System.Text.Encoding.UTF8))
			{
				// Rate Limit Status
				{
					var rls = tokens.Application.RateLimitStatus().RateLimit;
					Console.WriteLine(
						"Limit:{0} Remaining={1} Reset={2}",
						rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
						);
				}

				stream.WriteLine(__FUNCTION__);
				stream.WriteLine("====");

				foreach (var item in tokens.Friends.List())
				{
					//stream.WriteLine("## {0} @{1}  ", item.Name, item.ScreenName);
					stream.WriteLine("## {0}  ", string.Format("<a href=\"https://twitter.com/{1}\" target=_blank>{0} @{1}</a>", item.Name, item.ScreenName));
					stream.WriteLine("");
					if (string.IsNullOrWhiteSpace(item.ProfileBannerUrl) == false)
					{
						stream.WriteLine("<img src=\"{0}\">", item.ProfileBannerUrl);
						stream.WriteLine("");
					}
					stream.WriteLine("<table>");
					{
						stream.WriteLine("<tr>");
						stream.WriteLine("<td width=48>");
						if (string.IsNullOrWhiteSpace(item.ProfileImageUrl) == false)
						{
							stream.WriteLine("<a href=\"https://twitter.com/{1}\" target=_blank><img src=\"{0}\"></a>", item.ProfileImageUrl, item.ScreenName);
						}
						stream.WriteLine("</td>");
					}
					{
						stream.WriteLine("<td>");
						stream.WriteLine("{0}", item.Description);
						stream.WriteLine("</td>");
						stream.WriteLine("</tr>");
					}
					stream.WriteLine("</table>");
					stream.WriteLine("");
					stream.WriteLine("Id: {0}  ", item.Id);
					stream.WriteLine("Date: {0}  ", item.CreatedAt.LocalDateTime);
					stream.WriteLine("Url: {0}  ", item.Url);
					stream.WriteLine("Email: {0}  ", item.Email);
					stream.WriteLine("Statuses: {0}  ", item.StatusesCount);
					stream.WriteLine("Friends: {0}  ", item.FriendsCount);
					stream.WriteLine("Followers: {0}  ", item.FollowersCount);
					stream.WriteLine("Favourites: {0}  ", item.FavouritesCount);
					stream.WriteLine("Banner: {0}  ", item.ProfileBannerUrl);
					stream.WriteLine("Image: {0}  ", item.ProfileImageUrl);
					stream.WriteLine("");
				}
			}
		}

		#endregion

		#region コマンド: (本日のタイムライン)

		/// <summary>
		/// 本日のタイムライン
		/// </summary>
		/// <param name="tokens">トークン</param>
		/// <param name="offset_days">本日から遡る日数 [0~]</param>
		static void Today(CoreTweet.Tokens tokens, int offset_days)
		{
			var __FUNCTION__ = MethodBase.GetCurrentMethod().Name;

			var users = new SortedDictionary<string, CoreTweet.User>();
			var archives = new Dictionary<long?, List<CoreTweet.Status>>();

			var now = DateTimeOffset.Now;

			#region 収集開始日時の計算:
			var origin = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
			if (offset_days > 0)
			{
				var offseted = now.AddDays(-offset_days);
				origin = new DateTimeOffset(offseted.Year, offseted.Month, offseted.Day, 0, 0, 0, now.Offset);
			}
			Console.WriteLine("current: {0} ({1})", now.ToString("yyyy/MM/dd HH:mm:ss"), now.Offset);
			Console.WriteLine("origin : {0} ({1}) ({2} days)", origin.ToString("yyyy/MM/dd HH:mm:ss"), origin.Offset, offset_days);
			#endregion

			#region ユーザーリストの生成:
			foreach (var user in tokens.Friends.List())
			{
				users[user.ScreenName] = user;
			}
			#endregion

			#region 収集:
			foreach (var user in users)
			{
				var tweet_status_list = new List<CoreTweet.Status>();
				archives[user.Value.Id] = tweet_status_list;

				Console.WriteLine("User: {0} ({1})", user.Value.ScreenName, user.Value.Name);

				#region 収集:
				try
				{
					#region Rate Limit Status
					{
						var rls = tokens.Application.RateLimitStatus().RateLimit;

						Console.WriteLine(
							"Limit:{0} Remaining={1} Reset={2}",
							rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
							);
					}
					#endregion

					long? prev_id = null;
					while (true)
					{
						// Get
						var result = tokens.Statuses.UserTimeline(screen_name: user.Value.ScreenName, count: 200, max_id: prev_id);
						long? last_id = null;
						int total = 0;
						bool abort = false;
						if (result != null)
						{
							total = result.Count;
							foreach (var item in result)
							{
								if (item.CreatedAt < origin)
								{
									abort = true;
									break;
								}

								tweet_status_list.Add(item);
								last_id = item.Id;
							}
						}
						// Result
						{
							// Rate Limit Status
							var rls = tokens.Application.RateLimitStatus().RateLimit;
							var text1 = string.Format(
									"Limit:{0} Remaining={1} Reset={2}",
									rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
								);

							// count
							var text2 = string.Format(
								"items={0} last_id:{1} prev_id:{2}",
								tweet_status_list.Count, last_id, prev_id
								);

							Console.WriteLine("{0} {1}", text1, text2);
						}

						if (total == 0) break;
						if (abort == true) break;
						if (last_id == null) break;
						if (last_id == prev_id) break;

						prev_id = last_id;
						System.Threading.Thread.Sleep(1000);
					}
				}
				catch (System.Exception ex)
				{
					Console.WriteLine(__FUNCTION__);
					Console.WriteLine(ex.Message);
					Console.WriteLine(ex.StackTrace);
					break;
				}
				#endregion
			}
			#endregion

			#region 出力:
			for (int day_index = 0; day_index <= offset_days; day_index++)
			{
				var offseted = now.AddDays(-day_index);
				var origin_st = new DateTimeOffset(offseted.Year, offseted.Month, offseted.Day, 0, 0, 0, now.Offset);
				var origin_ed = origin_st + TimeSpan.FromDays(1);

				//var origin_lct = origin_st.LocalDateTime;
				var suffix_origin = string.Format("{0:0000}{1:00}{2:00}", origin_st.Year, origin_st.Month, origin_st.Day);
				var suffix = MakeFileNameSuffix(now.LocalDateTime, true);
				var filename = string.Format("{0}_{1}-{2}.md", __FUNCTION__, suffix_origin, suffix);
				using (var stream = new StreamWriter(filename, false, System.Text.Encoding.UTF8))
				{
					stream.WriteLine(__FUNCTION__);
					stream.WriteLine("====");

					#region 出力:
					foreach (var user in users)
					{
						var user_id = user.Value.Id;
						var articles = archives[user_id];

						stream.WriteLine("--------------------------------------------------");
						stream.WriteLine("# {0} @{1} ({2})", user.Value.Name, user.Value.ScreenName, user_id);
						stream.WriteLine("");

						var items = articles.FindAll((item) => { return (origin_st <= item.CreatedAt && item.CreatedAt < origin_ed); });
						foreach (var item in items)
						{
							stream.WriteLine("## {0}", item.CreatedAt.LocalDateTime);
							stream.WriteLine("");
							stream.WriteLine("<table>");
							stream.WriteLine("<tr>");
							stream.WriteLine("<td>");
							var texts = item.Text.Split('\n');
							foreach (var text in texts)
							{
								var line = text.ReplaceTag(item.Entities);
								stream.WriteLine("{0}<br/>", line);
							}
							stream.WriteLine("</td>");
							stream.WriteLine("</tr>");
							stream.WriteLine("</table>");
							stream.WriteLine("");
							stream.WritePhoto(item.Entities);
							stream.WriteLine("");
							stream.WriteLine("User: {0}  ", string.Format("<a href=\"https://twitter.com/{1}\" target=_blank>{0} @{1}</a>", item.User.Name, item.User.ScreenName));
							stream.WriteLine("Id: {0}  ", item.Id);
							stream.WriteLine("Retweet :{0} {1}  ", item.RetweetCount, ((item.IsRetweeted == true) ? "*" : ""));
							stream.WriteLine("Favorite:{0} {1}  ", item.FavoriteCount, ((item.IsFavorited == true) ? "*" : ""));
							stream.WriteLine("");
							stream.WriteEntities(item.Entities);
							stream.WriteLine("");
						}
						stream.WriteLine("");
					}
					#endregion
				}
			}
			#endregion
		}

		#endregion

		#region コマンド: (ホームタイムライン)

		/// <summary>
		/// ホームタイムライン
		/// </summary>
		/// <param name="tokens">トークン</param>
		/// <param name="offset_days">本日から遡る日数 [0~]</param>
		static void HomeTimeline(CoreTweet.Tokens tokens, int offset_days)
		{
			var __FUNCTION__ = MethodBase.GetCurrentMethod().Name;

			var tweet_status_list = new List<CoreTweet.Status>();
			var now = DateTimeOffset.Now;

			#region Rate Limit Status
			{
				var rls = tokens.Application.RateLimitStatus().RateLimit;
				Console.WriteLine(
					"Limit:{0} Remaining={1} Reset={2}",
					rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
					);
			}
			#endregion

			#region 収集開始日時の計算:
			var origin = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
			if (offset_days > 0)
			{
				var offseted = now.AddDays(-offset_days);
				origin = new DateTimeOffset(offseted.Year, offseted.Month, offseted.Day, 0, 0, 0, now.Offset);
			}
			Console.WriteLine("current: {0} ({1})", now.ToString("yyyy/MM/dd HH:mm:ss"), now.Offset);
			Console.WriteLine("origin : {0} ({1}) ({2} days)", origin.ToString("yyyy/MM/dd HH:mm:ss"), origin.Offset, offset_days);
			#endregion

			#region 収集:
			try
			{
				#region Rate Limit Status
				{
					var rls = tokens.Application.RateLimitStatus().RateLimit;

					Console.WriteLine(
						"Limit:{0} Remaining={1} Reset={2}",
						rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
						);
				}
				#endregion

				long? prev_id = null;
				while (true)
				{
					// 取得:
					var result = tokens.Statuses.HomeTimeline(count: 200, max_id: prev_id);

					long? last_id = null;
					int total = 0;
					bool abort = false;
					if (result != null)
					{
						total = result.Count;
						foreach (var item in result)
						{
							if (item.CreatedAt < origin)
							{
								abort = true;
								break;
							}

							tweet_status_list.Add(item);
							last_id = item.Id;
						}
					}
					// Result
					{
						// Rate Limit Status
						var rls = tokens.Application.RateLimitStatus().RateLimit;
						var text1 = string.Format(
								"Limit:{0} Remaining={1} Reset={2}",
								rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
							);

						// count
						var text2 = string.Format(
							"items={0} last_id:{1} prev_id:{2}",
							tweet_status_list.Count, last_id, prev_id
							);

						Console.WriteLine("{0} {1}", text1, text2);
					}

					if (total == 0) break;
					if (abort == true) break;
					if (last_id == null) break;
					if (last_id == prev_id) break;

					prev_id = last_id;
					System.Threading.Thread.Sleep(1000);
				}
			}
			catch (System.Exception ex)
			{
				Console.WriteLine(__FUNCTION__);
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
			}
			#endregion

			#region 出力:
			var suffix = MakeFileNameSuffix(now.LocalDateTime, true);
			var filename = string.Format("{0}-{1}.md", __FUNCTION__, suffix);
			using (var stream = new StreamWriter(filename, false, System.Text.Encoding.UTF8))
			{
				stream.WriteLine(__FUNCTION__);
				stream.WriteLine("====");

				stream.WriteLine("");
				stream.WriteLine("current: {0} ({1})  ", now.ToString("yyyy/MM/dd HH:mm:ss"), now.Offset);
				stream.WriteLine("origin: {0} ({1}) ({2} days)  ", origin.ToString("yyyy/MM/dd HH:mm:ss"), origin.Offset, offset_days);
				stream.WriteLine("");

				stream.WriteLine("# Result");
				stream.WriteLine("");

				foreach (var item in tweet_status_list)
				{
					stream.WriteLine("## {0}: {1} @{2}", item.CreatedAt.LocalDateTime, item.User.Name, item.User.ScreenName);
					stream.WriteLine("");
					stream.WriteLine("<table>");
					stream.WriteLine("<tr>");
					stream.WriteLine("<td>");
					var texts = item.Text.Split('\n');
					foreach (var text in texts)
					{
						var line = text.ReplaceTag(item.Entities);
						stream.WriteLine("{0}<br/>", line);
					}
					stream.WriteLine("</td>");
					stream.WriteLine("</tr>");
					stream.WriteLine("</table>");
					stream.WriteLine("");
					stream.WritePhoto(item.Entities);
					stream.WriteLine("");
					stream.WriteLine("User: {0}  ", string.Format("<a href=\"https://twitter.com/{1}\" target=_blank>{0} @{1}</a>", item.User.Name, item.User.ScreenName));
					stream.WriteLine("Id: {0}  ", item.Id);
					stream.WriteLine("Retweet :{0} {1}  ", item.RetweetCount, ((item.IsRetweeted == true) ? "*" : ""));
					stream.WriteLine("Favorite:{0} {1}  ", item.FavoriteCount, ((item.IsFavorited == true) ? "*" : ""));
					stream.WriteLine("");
					stream.WriteEntities(item.Entities);
					stream.WriteLine("");
				}
			}
			#endregion
		}

		#endregion

		#region コマンド: (ユーザータイムライン)

		/// <summary>
		/// ユーザータイムライン
		/// </summary>
		/// <param name="tokens">トークン</param>
		/// <param name="name">対象ユーザーのスクリーン名</param>
		/// <param name="offset_days">本日から遡る日数 [0~]</param>
		static void UserTimeline(CoreTweet.Tokens tokens, string name, int offset_days)
		{
			var __FUNCTION__ = MethodBase.GetCurrentMethod().Name;

			var tweet_status_list = new List<CoreTweet.Status>();
			var now = DateTimeOffset.Now;

			#region Rate Limit Status
			{
				var rls = tokens.Application.RateLimitStatus().RateLimit;
				Console.WriteLine(
					"Limit:{0} Remaining={1} Reset={2}",
					rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
					);
			}
			#endregion

			#region 収集開始日時の計算:
			var origin = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
			if (offset_days > 0)
			{
				var offseted = now.AddDays(-offset_days);
				origin = new DateTimeOffset(offseted.Year, offseted.Month, offseted.Day, 0, 0, 0, now.Offset);
			}
			Console.WriteLine("current: {0} ({1})", now.ToString("yyyy/MM/dd HH:mm:ss"), now.Offset);
			Console.WriteLine("origin : {0} ({1}) ({2} days)", origin.ToString("yyyy/MM/dd HH:mm:ss"), origin.Offset, offset_days);
			#endregion

			#region 収集:
			try
			{
				#region Rate Limit Status
				{
					var rls = tokens.Application.RateLimitStatus().RateLimit;

					Console.WriteLine(
						"Limit:{0} Remaining={1} Reset={2}",
						rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
						);
				}
				#endregion

				long? prev_id = null;
				while (true)
				{
					// 取得:
					var result = tokens.Statuses.UserTimeline(screen_name: name, count: 200, max_id: prev_id);

					long? last_id = null;
					int total = 0;
					bool abort = false;
					if (result != null)
					{
						total = result.Count;
						foreach (var item in result)
						{
							if (item.CreatedAt < origin)
							{
								abort = true;
								break;
							}

							tweet_status_list.Add(item);
							last_id = item.Id;
						}
					}
					// Result
					{
						// Rate Limit Status
						var rls = tokens.Application.RateLimitStatus().RateLimit;
						var text1 = string.Format(
								"Limit:{0} Remaining={1} Reset={2}",
								rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
							);

						// count
						var text2 = string.Format(
							"items={0} last_id:{1} prev_id:{2}",
							tweet_status_list.Count, last_id, prev_id
							);

						Console.WriteLine("{0} {1}", text1, text2);
					}

					if (total == 0) break;
					if (abort == true) break;
					if (last_id == null) break;
					if (last_id == prev_id) break;

					prev_id = last_id;
					System.Threading.Thread.Sleep(1000);
				}
			}
			catch (System.Exception ex)
			{
				Console.WriteLine(__FUNCTION__);
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
			}
			#endregion

			#region 出力:
			var suffix = MakeFileNameSuffix(now.LocalDateTime, true);
			var filename = string.Format("{0}-{1}.md", __FUNCTION__, suffix);
			using (var stream = new StreamWriter(filename, false, System.Text.Encoding.UTF8))
			{
				stream.WriteLine(__FUNCTION__);
				stream.WriteLine("====");

				stream.WriteLine("");
				stream.WriteLine("screen_name:{0}  ", name);

				stream.WriteLine("");
				stream.WriteLine("current: {0} ({1})  ", now.ToString("yyyy/MM/dd HH:mm:ss"), now.Offset);
				stream.WriteLine("origin: {0} ({1}) ({2} days)  ", origin.ToString("yyyy/MM/dd HH:mm:ss"), origin.Offset, offset_days);
				stream.WriteLine("");

				bool header_printed = false;

				foreach (var item in tweet_status_list)
				{
					#region ヘッダー部:
					if (header_printed == false)
					{
						header_printed = true;

						stream.WriteLine("## {0}  ", string.Format("<a href=\"https://twitter.com/{1}\" target=_blank>{0} @{1}</a>", item.User.Name, item.User.ScreenName));
						stream.WriteLine("");
						if (string.IsNullOrWhiteSpace(item.User.ProfileBannerUrl) == false)
						{
							stream.WriteLine("<img src=\"{0}\">", item.User.ProfileBannerUrl);
							stream.WriteLine("");
						}
						stream.WriteLine("<table>");
						{
							stream.WriteLine("<tr>");
							stream.WriteLine("<td width=48>");
							if (string.IsNullOrWhiteSpace(item.User.ProfileImageUrl) == false)
							{
								stream.WriteLine("<a href=\"https://twitter.com/{1}\" target=_blank><img src=\"{0}\"></a>", item.User.ProfileImageUrl, item.User.ScreenName);
							}
							stream.WriteLine("</td>");
						}
						{
							stream.WriteLine("<td>");
							stream.WriteLine("{0}", item.User.Description);
							stream.WriteLine("</td>");
							stream.WriteLine("</tr>");
						}
						stream.WriteLine("</table>");
						stream.WriteLine("");
						stream.WriteLine("Id: {0}  ", item.User.Id);
						stream.WriteLine("Date: {0}  ", item.User.CreatedAt.LocalDateTime);
						stream.WriteLine("Url: {0}  ", item.User.Url);
						stream.WriteLine("Email: {0}  ", item.User.Email);
						stream.WriteLine("Statuses: {0}  ", item.User.StatusesCount);
						stream.WriteLine("Friends: {0}  ", item.User.FriendsCount);
						stream.WriteLine("Followers: {0}  ", item.User.FollowersCount);
						stream.WriteLine("Favourites: {0}  ", item.User.FavouritesCount);
						stream.WriteLine("Banner: {0}  ", item.User.ProfileBannerUrl);
						stream.WriteLine("Image: {0}  ", item.User.ProfileImageUrl);
						stream.WriteLine("");

						stream.WriteLine("# Result");
						stream.WriteLine("");
					}
					#endregion

					stream.WriteLine("## {0}:", item.CreatedAt.LocalDateTime);
					stream.WriteLine("");
					stream.WriteLine("<table>");
					stream.WriteLine("<tr>");
					stream.WriteLine("<td>");
					var texts = item.Text.Split('\n');
					foreach (var text in texts)
					{
						var line = text.ReplaceTag(item.Entities);
						stream.WriteLine("{0}<br/>", line);
					}
					stream.WriteLine("</td>");
					stream.WriteLine("</tr>");
					stream.WriteLine("</table>");
					stream.WriteLine("");
					stream.WritePhoto(item.Entities);
					stream.WriteLine("");
					stream.WriteLine("User: {0}  ", string.Format("<a href=\"https://twitter.com/{1}\" target=_blank>{0} @{1}</a>", item.User.Name, item.User.ScreenName));
					stream.WriteLine("Id: {0}  ", item.Id);
					stream.WriteLine("Retweet :{0} {1}  ", item.RetweetCount, ((item.IsRetweeted == true) ? "*" : ""));
					stream.WriteLine("Favorite:{0} {1}  ", item.FavoriteCount, ((item.IsFavorited == true) ? "*" : ""));
					stream.WriteLine("");
					stream.WriteEntities(item.Entities);
					stream.WriteLine("");
				}
			}
			#endregion
		}

		#endregion

		#region コマンド: (検索)

		/// <summary>
		/// 検索
		/// </summary>
		/// <param name="tokens">トークン</param>
		/// <param name="keyword">フィルタキーワード</param>
		/// <param name="offset_days">本日から遡る日数 [0~7]</param>
		static void Search(CoreTweet.Tokens tokens, string keyword, int offset_days)
		{
			var __FUNCTION__ = MethodBase.GetCurrentMethod().Name;

			var tweet_status_list = new List<CoreTweet.Status>();
			var now = DateTimeOffset.Now;

			#region Rate Limit Status
			{
				var rls = tokens.Application.RateLimitStatus().RateLimit;
				Console.WriteLine(
					"Limit:{0} Remaining={1} Reset={2}",
					rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
					);
			}
			#endregion

			#region 収集開始日時の計算:
			var origin = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
			if (offset_days > 0)
			{
				var offseted = now.AddDays(-offset_days);
				origin = new DateTimeOffset(offseted.Year, offseted.Month, offseted.Day, 0, 0, 0, now.Offset);
			}
			Console.WriteLine("current: {0} ({1})", now.ToString("yyyy/MM/dd HH:mm:ss"), now.Offset);
			Console.WriteLine("origin : {0} ({1}) ({2} days)", origin.ToString("yyyy/MM/dd HH:mm:ss"), origin.Offset, offset_days);
			#endregion

			#region 収集:
			try
			{
				#region Rate Limit Status
				{
					var rls = tokens.Application.RateLimitStatus().RateLimit;

					Console.WriteLine(
						"Limit:{0} Remaining={1} Reset={2}",
						rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
						);
				}
				#endregion

				long? prev_id = null;
				while (true)
				{
					// 取得:
					var result = tokens.Search.Tweets(q: keyword, count: 200, max_id: prev_id);

					long? last_id = null;
					int total = 0;
					bool abort = false;
					if (result != null)
					{
						total = result.Count;
						foreach (var item in result)
						{
							if (item.CreatedAt < origin)
							{
								abort = true;
								break;
							}

							tweet_status_list.Add(item);
							last_id = item.Id;
						}
					}
					// Result
					{
						// Rate Limit Status
						var rls = tokens.Application.RateLimitStatus().RateLimit;
						var text1 = string.Format(
								"Limit:{0} Remaining={1} Reset={2}",
								rls.Limit, rls.Remaining, rls.Reset.LocalDateTime
							);

						// count
						var text2 = string.Format(
							"items={0} last_id:{1} prev_id:{2}",
							tweet_status_list.Count, last_id, prev_id
							);

						Console.WriteLine("{0} {1}", text1, text2);
					}

					if (total == 0) break;
					if (abort == true) break;
					if (last_id == null) break;
					if (last_id == prev_id) break;

					prev_id = last_id;
					System.Threading.Thread.Sleep(1000);
				}
			}
			catch (System.Exception ex)
			{
				Console.WriteLine(__FUNCTION__);
				Console.WriteLine(ex.Message);
				Console.WriteLine(ex.StackTrace);
			}
			#endregion

			#region 出力:
			var suffix = MakeFileNameSuffix(DateTime.Now, true);
			var filename = string.Format("{0}-{1}.md", __FUNCTION__, suffix);
			using (var stream = new StreamWriter(filename, false, System.Text.Encoding.UTF8))
			{
				stream.WriteLine(__FUNCTION__);
				stream.WriteLine("====");

				stream.WriteLine("");
				stream.WriteLine("keyword:{0}  ", keyword);

				stream.WriteLine("");
				stream.WriteLine("current: {0} ({1})  ", now.ToString("yyyy/MM/dd HH:mm:ss"), now.Offset);
				stream.WriteLine("origin: {0} ({1}) ({2} days)  ", origin.ToString("yyyy/MM/dd HH:mm:ss"), origin.Offset, offset_days);
				stream.WriteLine("");

				stream.WriteLine("# Result");
				stream.WriteLine("");

				foreach (var item in tweet_status_list)
				{
					stream.WriteLine("## {0}: {1} @{2}", item.CreatedAt.LocalDateTime, item.User.Name, item.User.ScreenName);
					stream.WriteLine("");
					stream.WriteLine("<table>");
					stream.WriteLine("<tr>");
					stream.WriteLine("<td>");
					var texts = item.Text.Split('\n');
					foreach (var text in texts)
					{
						var line = text.ReplaceTag(item.Entities);
						stream.WriteLine("{0}<br/>", line);
					}
					stream.WriteLine("</td>");
					stream.WriteLine("</tr>");
					stream.WriteLine("</table>");
					stream.WriteLine("");
					stream.WritePhoto(item.Entities);
					stream.WriteLine("");
					stream.WriteLine("User: {0}  ", string.Format("<a href=\"https://twitter.com/{1}\" target=_blank>{0} @{1}</a>", item.User.Name, item.User.ScreenName));
					stream.WriteLine("Id: {0}  ", item.Id);
					stream.WriteLine("Retweet :{0} {1}  ", item.RetweetCount, ((item.IsRetweeted == true) ? "*" : ""));
					stream.WriteLine("Favorite:{0} {1}  ", item.FavoriteCount, ((item.IsFavorited == true) ? "*" : ""));
					stream.WriteLine("");
					stream.WriteEntities(item.Entities);
					stream.WriteLine("");
				}
			}
			#endregion
		}

		#endregion
	}
}
