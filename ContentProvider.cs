﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Dropbox.Api;
using Dropbox.Api.Files;
using Dropbox.Api.Team;
using Sync_Storage_Creator_Windows.Properties;

namespace Sync_Storage_Creator_Windows
{
    class ContentProvider
    {

        private const string ApiKey = "776ru0jpqrbetf9";
        string remDir;
        string dir;

        public ContentProvider(string remDir, string dir)
        {
            this.remDir = remDir;
            this.dir = dir;
        }

        public static void init(string remDir, string dir)
        {
            var cont = new ContentProvider(remDir, dir);
            var task = Task.Run((Func<Task>)cont.Run);
            task.Wait();
        }

        public static string[] load()
        {
            var cont = new ContentProvider("", "");
            var task = Task.Run((Func<Task<string[]>>)cont.AllFolders);
            task.Wait();
            return task.Result;
        }

        public async Task Run()
        {
            string accessToken = await this.GetAccessToken();
            using (var dbx = new DropboxClient(accessToken))
            {
                var full = await dbx.Users.GetCurrentAccountAsync();
                Console.WriteLine("{0} - {1}", full.Name.DisplayName, full.Email);

                Dictionary<string, int> files = await Compare(dbx, dir, remDir);
                await Sync(dbx, remDir);
            }
        }

        async Task<Dictionary<string, int>> Compare(DropboxClient dbx, string path, string dpath)
        {
            Dictionary<string, int> ret = new Dictionary<string, int>();
            Dictionary<string, DateTime> Hfiles = new Dictionary<string, DateTime>();
            try
            {
                string[] file = System.IO.Directory.GetFiles(path + dpath);
                foreach (string i in file)
                {
                    Hfiles.Add(i.Replace("/", "\\"), File.GetLastWriteTime(i));
                }
            }catch(Exception e) { }
            Dictionary<string, DateTime> Dfiles = new Dictionary<string, DateTime>();
            try
            {
                var list = await dbx.Files.ListFolderAsync(dpath.ToLower());
                foreach (var item in list.Entries.Where(i => i.IsFile))
                {
                    Dfiles.Add(item.PathDisplay, item.AsFile.ServerModified);
                }
            }catch(Exception e) { }

            foreach(var Hfile in Hfiles)
            {
                string DfilePath = Hfile.Key.Replace(path, "").Replace("\\", "/");
                if (Dfiles.ContainsKey(DfilePath))
                {
                    DateTime date;
                    Dfiles.TryGetValue(DfilePath, out date);
                    if (date.Subtract(Hfile.Value).TotalMinutes < 0)
                    {
                        ret.Add(Hfile.Key, 0);
                    }
                    else
                    {
                        ret.Add(DfilePath, 1);
                    }
                }
                else
                {
                    ret.Add(Hfile.Key, 0);
                }
            }
            
            foreach(var Dfile in Dfiles)
            {
                string HfilePath = Dfile.Key.Insert(0, path).Replace("/", "\\");
                if (!Hfiles.ContainsKey(HfilePath))
                {
                    ret.Add(Dfile.Key, 1);
                }
            }
            return ret;
        }

        async Task Sync(DropboxClient dbx, string path)
        {
            var list = await dbx.Files.ListFolderAsync(path.ToLower(), recursive:true);

            foreach (var item in list.Entries.Where(i => i.IsFile))
            {
                string path2 = item.PathDisplay.Replace(item.Name, "");
                await Download(dbx, path2, item.Name);
            }
        }

        async Task Download(DropboxClient dbx, string folder, string file)
        {
            using (var response = await dbx.Files.DownloadAsync(folder + file))
            {
                System.IO.Directory.CreateDirectory(dir + folder);
                System.IO.File.WriteAllBytes(dir + folder + file, response.GetContentAsByteArrayAsync().Result);
            }
        }

        async Task Upload(DropboxClient dbx, string folder, string file, string content)
        {
            using (var mem = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                var updated = await dbx.Files.UploadAsync(
                    folder + "/" + file,
                    WriteMode.Overwrite.Instance,
                    body: mem);
                Console.WriteLine("Saved {0}/{1} rev {2}", folder, file, updated.Rev);
            }
        }

        async Task<string[]> AllFolders()
        {
            string accessToken = await this.GetAccessToken();
            using (var dbx = new DropboxClient(accessToken))
            {
                var list = await dbx.Files.ListFolderAsync(string.Empty);
                string[] ret = new string[list.Entries.Where(i => i.IsFolder).Count()];
                int g = 0;
                foreach (var item in list.Entries.Where(i => i.IsFolder))
                {
                    ret[g] = item.PathDisplay;
                    g++;
                }
                return ret;
            }
        }

        private async Task<string> GetAccessToken()
        {
            var accessToken = Settings.Default.AccessToken;

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("Waiting for credentials.");
                var completion = new TaskCompletionSource<Tuple<string, string>>();

                var thread = new Thread(() =>
                {
                    try
                    {
                        var login = new LoginForm(ApiKey);
                        login.ShowDialog();
                        if (login.Result)
                        {
                            completion.TrySetResult(Tuple.Create(login.AccessToken, login.Uid));
                        }
                        else
                        {
                            completion.TrySetCanceled();
                        }
                    }
                    catch (Exception e)
                    {
                        completion.TrySetException(e);
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                try
                {
                    var result = await completion.Task;
                    Console.WriteLine("and back...");

                    accessToken = result.Item1;
                    var uid = result.Item2;
                    Console.WriteLine("Uid: {0}", uid);

                    Settings.Default.AccessToken = accessToken;
                    Settings.Default.Uid = uid;

                    Settings.Default.Save();
                }
                catch (Exception e)
                {
                    e = e.InnerException ?? e;
                    Console.WriteLine("Error: {0}", e.Message);
                    return null;
                }
            }

            return accessToken;
        }

    }
}
