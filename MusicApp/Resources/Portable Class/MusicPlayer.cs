﻿using Android.App;
using Android.Content;
using Android.Database;
using Android.Gms.Cast;
using Android.Gms.Cast.Framework.Media;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Provider;
using Android.Support.V4.App;
using Android.Support.V4.Content;
using Android.Support.V4.Media.Session;
using Android.Support.V7.Preferences;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Widget;
using Com.Google.Android.Exoplayer2;
using Com.Google.Android.Exoplayer2.Extractor;
using Com.Google.Android.Exoplayer2.Source;
using Com.Google.Android.Exoplayer2.Source.Hls;
using Com.Google.Android.Exoplayer2.Trackselection;
using Com.Google.Android.Exoplayer2.Upstream;
using Com.Google.Android.Exoplayer2.Util;
using MusicApp.Resources.values;
using Newtonsoft.Json;
using Org.Json;
using SQLite;
using Square.Picasso;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Models;
using YoutubeExplode.Models.MediaStreams;
using static Android.Support.V4.Media.App.NotificationCompat;
using MediaInfo = Android.Gms.Cast.MediaInfo;
using MediaMetadata = Android.Gms.Cast.MediaMetadata;
using Uri = Android.Net.Uri;

namespace MusicApp.Resources.Portable_Class
{
    [Service]
    public class MusicPlayer : Service, IPlayerEventListener, AudioManager.IOnAudioFocusChangeListener
    {
        public static MusicPlayer instance;
        public static bool UseCastPlayer = false;
        public static SimpleExoPlayer player;
        public static RemoteMediaClient RemotePlayer;
        public static CastCallback CastCallback;
        public static CastQueueManager CastQueueManager;
        public float volume;
        private static AudioStopper noisyReceiver;
        public static List<Song> queue = new List<Song>();
        public static List<int> WaitForIndex = new List<int>();
        public MediaSessionCompat mediaSession;
        public AudioManager audioManager;
        public NotificationManager notificationManager;
        private bool noisyRegistered;
        public static bool isRunning = false;
        public static string title;
        private static bool parsing = false;
        private bool generating = false;
        public static int currentID = 0;
        public static bool autoUpdateSeekBar = true;
        public static bool repeat = false;
        public static bool useAutoPlay = false;
        public static bool userStopped = true;
        public static bool isLiveStream = false;
        public static bool ShouldResumePlayback;

        private static long LastTimer = -1;
        private Notification notification;
        private const int notificationID = 1000;
        private bool volumeDuked;

        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        public override void OnCreate()
        {
            base.OnCreate();
            instance = this;
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            if (intent == null)
            {
                RetrieveQueueFromDataBase();
                return StartCommandResult.Sticky;
            }

            string file = intent.GetStringExtra("file");

            switch (intent.Action)
            {
                case "YoutubePlay":
                    string action = intent.GetStringExtra("action");
                    string title = intent.GetStringExtra("title");
                    string artist = intent.GetStringExtra("artist");
                    string thumbnailURL = intent.GetStringExtra("thumbnailURI");
                    bool showPlayer = intent.GetBooleanExtra("showPlayer", true);
                    ParseAndPlay(action, file, title, artist, thumbnailURL, showPlayer);
                    break;

                case "Previus":
                    PlayPrevious();
                    break;

                case "Pause":
                    if(isRunning)
                        Pause(true);
                    else
                        Resume();
                    break;

                case "ForcePause":
                    if (isRunning)
                        Pause(true);
                    break;

                case "ForceResume":
                    Resume();
                    Player.errorState = false;
                    break;

                case "Next":
                    PlayNext();
                    break;

                case "RandomPlay":
                    List<string> files = intent.GetStringArrayListExtra("files").ToList();
                    bool clearQueue = intent.GetBooleanExtra("clearQueue", true);
                    RandomPlay(files, clearQueue);
                    break;

                case "RandomizeQueue":
                    RandomizeQueue();
                    break;

                case "PlayNext":
                    AddToQueue(file);
                    break;

                case "PlayLast":
                    PlayLastInQueue(file);
                    break;

                case "Stop":
                    Stop();
                    break;

                case "SleepPause":
                    SleepPause();
                    break;

                case "SwitchQueue":
                    SwitchQueue(intent.GetIntExtra("queueSlot", -1), intent.GetBooleanExtra("showPlayer", true));
                    break;

                case "CastListener":
                    if (CastCallback == null)
                        InitializeService();
                    CastCallback.OnStatusUpdated();
                    return StartCommandResult.Sticky;
            }

            if (intent.Action != null)
                return StartCommandResult.Sticky;

            if (file != null && file != "")
                Play(file);

            return StartCommandResult.Sticky;
        }

        public async static Task<Song> GetItem(int position = -2)
        {
            if (position == -2)
                position = CurrentID();

            if (position >= queue.Count || position < 0)
                return null;

            if (queue[position] == null && !WaitForIndex.Contains(position))
            {
                RemotePlayer.MediaQueue.GetItemAtIndex(position, true);
                WaitForIndex.Add(position);
            }

            while (queue[position] == null)
                await Task.Delay(100);

            return queue[position];
        }

        private void InitializeService()
        {
            audioManager = (AudioManager)Application.Context.GetSystemService(AudioService);
            notificationManager = (NotificationManager)Application.Context.GetSystemService(NotificationService);
            ISharedPreferences prefManager = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
            AdaptiveTrackSelection.Factory trackSelectionFactory = new AdaptiveTrackSelection.Factory(new DefaultBandwidthMeter());
            TrackSelector trackSelector = new DefaultTrackSelector(trackSelectionFactory);
            player = ExoPlayerFactory.NewSimpleInstance(Application.Context, trackSelector);
            volume = prefManager.GetInt("volumeMultiplier", 100) / 100f;
            player.Volume = volume;
            player.AddListener(this);

            if (noisyReceiver == null)
                noisyReceiver = new AudioStopper();

            RegisterReceiver(noisyReceiver, new IntentFilter(AudioManager.ActionAudioBecomingNoisy));
            noisyRegistered = true;

            RemotePlayer = MainActivity.CastContext.SessionManager.CurrentCastSession?.RemoteMediaClient;
            if(RemotePlayer != null)
            {
                if (CastCallback == null)
                {
                    CastCallback = new CastCallback();
                    RemotePlayer.RegisterCallback(CastCallback);
                }
                if(CastQueueManager == null)
                {
                    CastQueueManager = new CastQueueManager();
                    RemotePlayer.MediaQueue.RegisterCallback(CastQueueManager);
                }
            }
            UseCastPlayer = RemotePlayer != null;
            player.PlayWhenReady = !UseCastPlayer;
        }

        public void ChangeVolume(float volume)
        {
            if(player != null)
                player.Volume = volume * (volumeDuked ? 0.2f : 1);
        }

        public void Play(string filePath, string title = null, string artist = null, string youtubeID = null, string thumbnailURI = null, bool isLive = false, DateTimeOffset? expireDate = null)
        {
            isRunning = true;
            if (player == null)
                InitializeService();

            Song song = null;
            if (title == null)
                song = Browse.GetSong(filePath);
            else
            {
                song = new Song(title, artist, thumbnailURI, youtubeID, -1, -1, filePath, true);
            }

            song.IsLiveStream = isLive;
            isLiveStream = isLive;

            if (!UseCastPlayer)
            {
                if (mediaSession == null)
                {
                    mediaSession = new MediaSessionCompat(Application.Context, "MusicApp");
                    mediaSession.SetFlags(MediaSessionCompat.FlagHandlesMediaButtons | MediaSessionCompat.FlagHandlesTransportControls);
                    PlaybackStateCompat.Builder builder = new PlaybackStateCompat.Builder().SetActions(PlaybackStateCompat.ActionPlay | PlaybackStateCompat.ActionPause | PlaybackStateCompat.ActionSkipToNext | PlaybackStateCompat.ActionSkipToPrevious);
                    mediaSession.SetPlaybackState(builder.Build());
                    mediaSession.SetCallback(new HeadphonesActions());
                }

                DefaultDataSourceFactory dataSourceFactory = new DefaultDataSourceFactory(Application.Context, "MusicApp");
                IExtractorsFactory extractorFactory = new DefaultExtractorsFactory();
                Handler handler = new Handler();

                IMediaSource mediaSource = null;
                if (isLive)
                    mediaSource = new HlsMediaSource(Uri.Parse(filePath), dataSourceFactory, handler, null);
                else if (title == null)
                    mediaSource = new ExtractorMediaSource(Uri.FromFile(new Java.IO.File(filePath)), dataSourceFactory, extractorFactory, handler, null);
                else
                    mediaSource = new ExtractorMediaSource(Uri.Parse(filePath), dataSourceFactory, extractorFactory, handler, null);

                AudioAttributes attributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)
                    .SetContentType(AudioContentType.Music)
                    .Build();

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    AudioFocusRequestClass focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                        .SetAudioAttributes(attributes)
                        .SetAcceptsDelayedFocusGain(true)
                        .SetWillPauseWhenDucked(true)
                        .SetOnAudioFocusChangeListener(this)
                        .Build();
                    AudioFocusRequest audioFocus = audioManager.RequestAudioFocus(focusRequest);

                    if (audioFocus != AudioFocusRequest.Granted)
                    {
                        Console.WriteLine("Can't Get Audio Focus");
                        return;
                    }
                }
                else
                {
#pragma warning disable CS0618 // Type or member is obsolete

                    AudioManager am = (AudioManager)MainActivity.instance.GetSystemService(AudioService);

                    AudioFocusRequest audioFocus = am.RequestAudioFocus(this, Stream.Music, AudioFocus.Gain);

                    if (audioFocus != AudioFocusRequest.Granted)
                    {
                        Console.WriteLine("Can't Get Audio Focus");
                        return;
                    }
#pragma warning restore CS0618
                }

                player.PlayWhenReady = true;
                player.Prepare(mediaSource, true, true);
                CreateNotification(song.Title, song.Artist, song.AlbumArt, song.Album);
                AddToQueue(song);

                UpdateQueueSlots();
                currentID = CurrentID() + 1;
            }
            else
            {
                RemotePlayer.Load(GetMediaInfo(song), new MediaLoadOptions.Builder().SetAutoplay(true).Build());
                RemotePlayer.Play();
                queue = new List<Song> { song };
                currentID = 0;
                Console.WriteLine("&Song inserted in the queue");
            }

            SaveQueueSlot();
            Player.instance?.RefreshPlayer();
            Home.instance?.AddQueue();
            Queue.instance?.RefreshCurrent();
            ParseNextSong();
        }

        public void Play(Song song, long progress = -1)
        {
            if (!song.IsParsed)
            {
                ParseAndPlay("Play", song.YoutubeID, song.Title, song.Artist, song.Album);
                return;
            }

            isLiveStream = song.IsLiveStream;

            isRunning = true;
            if (player == null)
                InitializeService();

            if (!UseCastPlayer)
            {
                if (mediaSession == null)
                {
                    mediaSession = new MediaSessionCompat(Application.Context, "MusicApp");
                    mediaSession.SetFlags(MediaSessionCompat.FlagHandlesMediaButtons | MediaSessionCompat.FlagHandlesTransportControls);
                    PlaybackStateCompat.Builder builder = new PlaybackStateCompat.Builder().SetActions(PlaybackStateCompat.ActionPlay | PlaybackStateCompat.ActionPause | PlaybackStateCompat.ActionSkipToNext | PlaybackStateCompat.ActionSkipToPrevious);
                    mediaSession.SetPlaybackState(builder.Build());
                    mediaSession.SetCallback(new HeadphonesActions());
                }

                DefaultDataSourceFactory dataSourceFactory = new DefaultDataSourceFactory(Application.Context, "MusicApp");
                IExtractorsFactory extractorFactory = new DefaultExtractorsFactory();
                Handler handler = new Handler();
                IMediaSource mediaSource;

                if (!song.IsYt)
                    mediaSource = new ExtractorMediaSource(Uri.FromFile(new Java.IO.File(song.Path)), dataSourceFactory, extractorFactory, handler, null);
                else
                    mediaSource = new ExtractorMediaSource(Uri.Parse(song.Path), dataSourceFactory, extractorFactory, handler, null);

                AudioAttributes attributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)
                    .SetContentType(AudioContentType.Music)
                    .Build();

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    AudioFocusRequestClass focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                        .SetAudioAttributes(attributes)
                        .SetAcceptsDelayedFocusGain(true)
                        .SetWillPauseWhenDucked(true)
                        .SetOnAudioFocusChangeListener(this)
                        .Build();
                    AudioFocusRequest audioFocus = audioManager.RequestAudioFocus(focusRequest);

                    if (audioFocus != AudioFocusRequest.Granted)
                    {
                        Console.WriteLine("Can't Get Audio Focus");
                        return;
                    }
                }
                else
                {
#pragma warning disable CS0618 // Type or member is obsolete

                    AudioManager am = (AudioManager)MainActivity.instance.GetSystemService(AudioService);

                    AudioFocusRequest audioFocus = am.RequestAudioFocus(this, Stream.Music, AudioFocus.Gain);

                    if (audioFocus != AudioFocusRequest.Granted)
                    {
                        Console.WriteLine("Can't Get Audio Focus");
                        return;
                    }
#pragma warning restore CS0618
                }

                player.PlayWhenReady = true;
                player.Prepare(mediaSource, true, true);
                CreateNotification(song.Title, song.Artist, song.AlbumArt, song.Album);
            }
            else
            {
                RemotePlayer.Load(GetMediaInfo(song), new MediaLoadOptions.Builder().SetAutoplay(true).Build());
                RemotePlayer.Play();
            }

            isRunning = true;
            AddToQueue(song);
            UpdateQueueSlots();
            currentID = CurrentID() + 1;

            if (progress != -1)
            {
                player.SeekTo(progress);
                MainActivity.instance?.FindViewById<ImageButton>(Resource.Id.playButton).SetImageResource(Resource.Drawable.Pause);
            }

            SaveQueueSlot();
            Player.instance?.RefreshPlayer();
            Home.instance?.AddQueue();
            Queue.instance?.RefreshCurrent();
            ParseNextSong();
        }

        public static void UpdateQueueSlots()
        {
            //for (int i = 0; i < queue.Count; i++)
            //{
            //    queue[i].QueueSlot = i;
            //}
            UpdateQueueDataBase();
        }

        private async void ParseAndPlay(string action, string videoID, string title, string artist, string thumbnailURL, bool showPlayer = true)
        {
            if (!parsing)
            {
                parsing = true;

                if (MainActivity.instance != null && action == "Play")
                {
                    ProgressBar parseProgress = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.ytProgress);
                    parseProgress.Visibility = ViewStates.Visible;
                    parseProgress.ScaleY = 6;
                    Player.instance.Buffering();
                }

                try
                {
                    YoutubeClient client = new YoutubeClient();
                    var mediaStreamInfo = await client.GetVideoMediaStreamInfosAsync(videoID);
                    AudioStreamInfo streamInfo = mediaStreamInfo.Audio.OrderBy(s => s.Bitrate).Last();
                    bool isLive = false;
                    string streamURL = streamInfo.Url;
                    if (mediaStreamInfo.HlsLiveStreamUrl != null)
                    {
                        streamURL = mediaStreamInfo.HlsLiveStreamUrl;
                        isLive = true;
                    }


                    if (title == null)
                    {
                        Video video = await client.GetVideoAsync(videoID);
                        title = video.Title;
                        artist = video.Author;
                        thumbnailURL = video.Thumbnails.HighResUrl;
                    }

                    Console.WriteLine("&Use Cast Player: " + UseCastPlayer);
                    DateTimeOffset? expireDate = null;

                    if (UseCastPlayer)
                    {
                        Video info = await client.GetVideoAsync(videoID);
                        thumbnailURL = info.Thumbnails.HighResUrl;
                        if (artist == null || artist == "")
                            artist = info.Author;

                        if (!isLive)
                        {
                            expireDate = mediaStreamInfo.ValidUntil;
                        }
                    }

                    Console.WriteLine("&Starting playback");

                    switch (action)
                    {
                        case "Play":
                            Play(streamURL, title, artist, videoID, thumbnailURL, isLive, expireDate);
                            break;

                        case "PlayNext":
                            AddToQueue(streamURL, title, artist, videoID, thumbnailURL, isLive);
                            parsing = false;
                            return;

                        case "PlayLast":
                            PlayLastInQueue(streamURL, title, artist, videoID, thumbnailURL, isLive);
                            parsing = false;
                            return;
                    }

                    Console.WriteLine("&Action skipped");
                    if (!UseCastPlayer)
                    {
                        Video info = await client.GetVideoAsync(videoID);
                        thumbnailURL = info.Thumbnails.HighResUrl;
                        if (artist == null || artist == "")
                            artist = info.Author;

                        queue[CurrentID()].Album = thumbnailURL;
                        queue[CurrentID()].Artist = artist;

                        if (!isLive)
                        {
                            expireDate = mediaStreamInfo.ValidUntil;
                            queue[CurrentID()].ExpireDate = expireDate;
                        }
                    }
                }
                catch (System.Net.Http.HttpRequestException)
                {
                    MainActivity.instance.Timout();
                    parsing = false;
                    if (MainActivity.instance != null)
                        MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.ytProgress).Visibility = ViewStates.Gone;
                    return;
                }
                //catch
                //{
                //    MainActivity.instance.Unknow();
                //    parsing = false;
                //    if (MainActivity.instance != null)
                //        MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.ytProgress).Visibility = ViewStates.Gone;
                //    return;
                //}

                Console.WriteLine("&Catch block exited");

                Player.instance?.RefreshPlayer();
                if (MainActivity.instance != null)
                {
                    MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.ytProgress).Visibility = ViewStates.Gone;
                    MainActivity.instance.ShowSmallPlayer();
                    MainActivity.instance.ShowPlayer();
                }
                UpdateQueueItemDB(await GetItem());
                parsing = false;
            }
        }

        /*async*/ void GenerateNext(int number)
        {
            if (generating == true)
                return;

            generating = true;

            //string youtubeID = null;
            //if (MainActivity.HasInternet())
            //{
            //    int i = 1;
            //    while (youtubeID == null)
            //    {
            //        if (queue.Count >= i)
            //        {
            //            youtubeID = queue[queue.Count - i].youtubeID;
            //            i++;
            //        }
            //        else
            //            youtubeID = "local";
            //    }
            //}
            //else
            //    youtubeID = "local";

            //if (youtubeID != "local" && !await MainActivity.instance.WaitForYoutube())
            //{
            //        YoutubeClient client = new YoutubeClient();
            //        Video video = await client.GetVideoAsync(youtubeID);

            //        var ytPlaylistRequest = YoutubeEngine.youtubeService.PlaylistItems.List("snippet, contentDetails");
            //        ytPlaylistRequest.PlaylistId = video.GetVideoMixPlaylistId();
            //        ytPlaylistRequest.MaxResults = number + 2;

            //        var ytPlaylist = await ytPlaylistRequest.ExecuteAsync();

            //        foreach (var item in ytPlaylist.Items)
            //        {
            //            if (item.Snippet.Title != "[Deleted video]" && item.Snippet.Title != "Private video" && item.Snippet.Title != "Deleted video" && item.ContentDetails.VideoId != MusicPlayer.queue[MusicPlayer.CurrentID()].youtubeID)
            //            {
            //                Song song = new Song(item.Snippet.Title, "", item.Snippet.Thumbnails.Default__.Url, item.ContentDetails.VideoId, -1, -1, item.ContentDetails.VideoId, true, false);
            //                if(!queue.Exists(x => x.youtubeID == song.youtubeID))
            //                {
            //                    PlayLastInQueue(song);
            //                    break;
            //                }
            //            }
            //        }
            //    ParseNextSong();
            //}
            //else
            //{
                Uri musicUri = MediaStore.Audio.Media.ExternalContentUri;

                List<Song> allSongs = new List<Song>();
                Android.Content.CursorLoader cursorLoader = new Android.Content.CursorLoader(Application.Context, musicUri, null, null, null, null);
                ICursor musicCursor = (ICursor)cursorLoader.LoadInBackground();

                if (musicCursor != null && musicCursor.MoveToFirst())
                {
                    int titleID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Title);
                    int artistID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Artist);
                    int albumID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Album);
                    int thisID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Id);
                    int pathID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Data);
                    do
                    {
                        string Artist = musicCursor.GetString(artistID);
                        string Title = musicCursor.GetString(titleID);
                        string Album = musicCursor.GetString(albumID);
                        long AlbumArt = musicCursor.GetLong(musicCursor.GetColumnIndex(MediaStore.Audio.Albums.InterfaceConsts.AlbumId));
                        long id = musicCursor.GetLong(thisID);
                        string path = musicCursor.GetString(pathID);

                        if (Title == null)
                            Title = "Unknown Title";
                        if (Artist == null)
                            Artist = "Unknow Artist";
                        if (Album == null)
                            Album = "Unknow Album";

                        allSongs.Add(new Song(Title, Artist, Album, null, AlbumArt, id, path));
                    }
                    while (musicCursor.MoveToNext());
                    musicCursor.Close();
                }
                Random r = new Random();
                List<Song> songList = allSongs.OrderBy(x => r.Next()).ToList();
                for (int i = 0; i < (number > songList.Count ? songList.Count : number); i++)
                    PlayLastInQueue(songList[i]);
            //}

            Queue.instance?.Refresh();
            generating = false;
        }

        public async void RandomPlay(List<string> filePaths, bool clearQueue)
        {
            currentID = 0;
            if (clearQueue)
                queue.Clear();

            Random r = new Random();
            filePaths = filePaths.OrderBy(x => r.Next()).ToList();
            if (clearQueue)
            {
                Play(filePaths[0]);
                filePaths.RemoveAt(0);
            }

            foreach (string filePath in filePaths)
            {
                Song song = Browse.GetSong(filePath);
                queue.Add(song);
                await Task.Delay(10);
            }

            if (UseCastPlayer)
            {
                await RemotePlayer.QueueLoadAsync(queue.ConvertAll(GetQueueItem).ToArray(), 0, 0, null);
            }

            UpdateQueueDataBase();
            Home.instance?.RefreshQueue();
        }

        public async void RandomPlay(List<Song> songs, bool clearQueue)
        {
            currentID = 0;
            if (clearQueue)
                queue.Clear();

            Random r = new Random();
            songs = songs.OrderBy(x => r.Next()).ToList();
            if (clearQueue)
            {
                Play(songs[0]);
                songs.RemoveAt(0);
            }
            queue.AddRange(songs);

            if (UseCastPlayer)
            {
                await RemotePlayer.QueueLoadAsync(queue.ConvertAll(GetQueueItem).ToArray(), 0, 0, null);
            }

            UpdateQueueDataBase();
            Home.instance?.RefreshQueue();
        }

        private void RandomizeQueue()
        {
            if (UseCastPlayer)
            {
                RemotePlayer.QueueSetRepeatMode(MediaStatus.RepeatModeRepeatAllAndShuffle, null);
            }
            else
            {
                Random r = new Random();
                Song current = queue[CurrentID()];
                queue.RemoveAt(CurrentID());
                queue = queue.OrderBy(x => r.Next()).ToList();

                currentID = 0;
                queue.Insert(0, current);

                UpdateQueueSlots();
                SaveQueueSlot();
                Player.instance?.UpdateNext();
                Queue.instance?.Refresh();
                Home.instance?.RefreshQueue();
                Queue.instance?.ListView.ScrollToPosition(0);
            }
        }

        public void AddToQueue(string filePath, string title = null, string artist = null, string youtubeID = null, string thumbnailURI = null, bool isLive = false)
        {
            Song song = null;
            if(title == null)
                song = Browse.GetSong(filePath);
            else
                song = new Song(title, artist, thumbnailURI, youtubeID, -1, -1, filePath, true);

            song.IsLiveStream = isLive;
            queue.Insert(CurrentID() + 1, song);
            Home.instance?.RefreshQueue();

            if (UseCastPlayer)
            {
                if (RemotePlayer.CurrentItem != null)
                {
                    int currentIndex = (int)RemotePlayer.MediaStatus.GetIndexById(RemotePlayer.CurrentItem.ItemId);
                    if (currentIndex + 1 < RemotePlayer.MediaStatus.QueueItemCount)
                        RemotePlayer.QueueInsertItems(new MediaQueueItem[] { GetQueueItem(song) }, RemotePlayer.MediaQueue.ItemIdAtIndex(currentIndex + 1), null);
                    else
                        RemotePlayer.QueueAppendItem(GetQueueItem(song), null);
                }
                else
                    RemotePlayer.QueueAppendItem(GetQueueItem(song), null);
            }
            else
            {
                UpdateQueueSlots();
            }
        }

        public void AddToQueue(Song song)
        {
            queue.Insert(CurrentID() + 1, song);
            Home.instance?.RefreshQueue();

            if (!UseCastPlayer)
            {
                UpdateQueueSlots();
            }
            else
            {
                if (RemotePlayer.CurrentItem != null)
                {
                    int currentIndex = (int)RemotePlayer.MediaStatus.GetIndexById(RemotePlayer.CurrentItem.ItemId);
                    if (currentIndex + 1 < RemotePlayer.MediaStatus.QueueItemCount)
                        RemotePlayer.QueueInsertItems(new MediaQueueItem[] { GetQueueItem(song) }, RemotePlayer.MediaQueue.ItemIdAtIndex(currentIndex + 1), null);
                    else
                        RemotePlayer.QueueAppendItem(GetQueueItem(song), null);
                }
                else
                    RemotePlayer.QueueAppendItem(GetQueueItem(song), null);
            }
        }

        public void PlayLastInQueue(string filePath)
        {
            Song song = Browse.GetSong(filePath);

            if (UseCastPlayer)
                RemotePlayer.QueueAppendItem(GetQueueItem(song), null);
            else
            {
                queue.Add(song);
                UpdateQueueItemDB(song);

                Home.instance?.RefreshQueue();
            }
        }

        public void PlayLastInQueue(Song song)
        {
            if (UseCastPlayer)
                RemotePlayer.QueueAppendItem(GetQueueItem(song), null);
            else
            {
                queue.Add(song);
                UpdateQueueItemDB(song);
                Home.instance?.RefreshQueue();
            }
        }

        public void PlayLastInQueue(string filePath, string title, string artist, string youtubeID, string thumbnailURI, bool isLive = false)
        {
            Song song = new Song(title, artist, thumbnailURI, youtubeID, -1, -1, filePath, true)
            {
                IsLiveStream = isLive
            };


            if (UseCastPlayer)
                RemotePlayer.QueueAppendItem(GetQueueItem(song), null);
            else
            {
                queue.Add(song);
                UpdateQueueItemDB(song);
                Home.instance?.RefreshQueue();
            }
        }

        public void PlayPrevious()
        {
            Player.instance.playNext = false;
            if(CurrentPosition > Duration * 0.2f || CurrentID() - 1 < 0)
            {
                if (player != null)
                    player.SeekTo(0);
                else
                    SwitchQueue(CurrentID(), false, false);
                return;
            }

            if (UseCastPlayer)
                RemotePlayer.QueuePrev(null);
            else
                SwitchQueue(CurrentID() - 1);
        }

        public void PlayNext()
        {
            Player.instance.playNext = true;
            if (CurrentID() + 1 > queue.Count - 1 || CurrentID() == -1)
            {
                if (repeat)
                {
                    SwitchQueue(0);
                    return;
                }
                else
                {
                    Pause(true);
                    return;
                }
            }

            if (UseCastPlayer)
                RemotePlayer.QueueNext(null);
            else
                SwitchQueue(CurrentID() + 1);
            
            if (useAutoPlay && CurrentID() + 3 > queue.Count)
            {
                GenerateNext(1);
            }
        }

        public async void SwitchQueue(int position, bool showPlayer = false, bool StartFromOldPosition = true)
        {
            Song song = await GetItem(position);
            if (!song.IsParsed)
            {
                Player.instance?.Buffering();
                if (MainActivity.instance != null && showPlayer)
                {
                    ProgressBar parseProgress = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.ytProgress);
                    parseProgress.Visibility = ViewStates.Visible;
                    parseProgress.ScaleY = 6;
                }
                try
                {
                    YoutubeClient client = new YoutubeClient();
                    MediaStreamInfoSet mediaStreamInfo = await client.GetVideoMediaStreamInfosAsync(song.YoutubeID);
                    AudioStreamInfo streamInfo = mediaStreamInfo.Audio.Where(x => x.Container == Container.Mp4).OrderBy(s => s.Bitrate).Last();
                    song.Path = streamInfo.Url;
                    song.IsParsed = true;
                    if (Queue.instance != null)
                    {
                        int item = queue.IndexOf(song);
                        int firstItem = ((LinearLayoutManager)Queue.instance.ListView.GetLayoutManager()).FindFirstVisibleItemPosition();
                        int lastItem = ((LinearLayoutManager)Queue.instance.ListView.GetLayoutManager()).FindLastVisibleItemPosition();
                        if (firstItem < item && item < lastItem)
                        {
                            ImageView youtubeIcon = Queue.instance.ListView.GetChildAt(item - firstItem).FindViewById<ImageView>(Resource.Id.youtubeIcon);
                            youtubeIcon.SetImageResource(Resource.Drawable.PublicIcon);
                        }
                    }

                    DateTimeOffset expireDate = mediaStreamInfo.ValidUntil;
                    song.ExpireDate = expireDate;

                    Video info = await client.GetVideoAsync(song.YoutubeID);
                    song.Album = info.Thumbnails.HighResUrl;
                    song.Artist = info.Author;
                    UpdateQueueItemDB(song);
                }
                catch (System.Net.Http.HttpRequestException)
                {
                    MainActivity.instance.Timout();
                    return;
                }
                catch
                {
                    MainActivity.instance.Unknow();
                    return;
                }

                if (MainActivity.instance != null && showPlayer)
                {
                    ProgressBar parseProgress = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.ytProgress);
                    parseProgress.Visibility = ViewStates.Gone;
                }
            }

            if (UseCastPlayer)
                RemotePlayer.QueueJumpToItem(RemotePlayer.MediaStatus.GetItemByIndex(position).ItemId, null);
            else
            {
                isLiveStream = song.IsLiveStream;

                isRunning = true;
                if (player == null)
                    InitializeService();

                if (mediaSession == null)
                {
                    mediaSession = new MediaSessionCompat(Application.Context, "MusicApp");
                    mediaSession.SetFlags(MediaSessionCompat.FlagHandlesMediaButtons | MediaSessionCompat.FlagHandlesTransportControls);
                    PlaybackStateCompat.Builder builder = new PlaybackStateCompat.Builder().SetActions(PlaybackStateCompat.ActionPlay | PlaybackStateCompat.ActionPause | PlaybackStateCompat.ActionSkipToNext | PlaybackStateCompat.ActionSkipToPrevious);
                    mediaSession.SetPlaybackState(builder.Build());
                    mediaSession.SetCallback(new HeadphonesActions());
                }

                DefaultDataSourceFactory dataSourceFactory = new DefaultDataSourceFactory(Application.Context, "MusicApp");
                IExtractorsFactory extractorFactory = new DefaultExtractorsFactory();
                Handler handler = new Handler();
                IMediaSource mediaSource;

                if (!song.IsYt)
                    mediaSource = new ExtractorMediaSource(Uri.FromFile(new Java.IO.File(song.Path)), dataSourceFactory, extractorFactory, handler, null);
                else
                    mediaSource = new ExtractorMediaSource(Uri.Parse(song.Path), dataSourceFactory, extractorFactory, handler, null);

                AudioAttributes attributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)
                    .SetContentType(AudioContentType.Music)
                    .Build();

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    AudioFocusRequestClass focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                        .SetAudioAttributes(attributes)
                        .SetAcceptsDelayedFocusGain(true)
                        .SetWillPauseWhenDucked(true)
                        .SetOnAudioFocusChangeListener(this)
                        .Build();
                    AudioFocusRequest audioFocus = audioManager.RequestAudioFocus(focusRequest);

                    if (audioFocus != AudioFocusRequest.Granted)
                    {
                        Console.WriteLine("Can't Get Audio Focus");
                        return;
                    }
                }
                else
                {
#pragma warning disable CS0618 // Type or member is obsolete

                    AudioManager am = (AudioManager)MainActivity.instance.GetSystemService(AudioService);

                    AudioFocusRequest audioFocus = am.RequestAudioFocus(this, Stream.Music, AudioFocus.Gain);

                    if (audioFocus != AudioFocusRequest.Granted)
                    {
                        Console.WriteLine("Can't Get Audio Focus");
                        return;
                    }
#pragma warning restore CS0618
                }

                player.PlayWhenReady = true;
                player.Prepare(mediaSource, true, true);
                CreateNotification(song.Title, song.Artist, song.AlbumArt, song.Album);
                isRunning = true;

                if (currentID == position && StartFromOldPosition)
                    player.SeekTo(LastTimer);

                currentID = position;

                SaveQueueSlot();
                Player.instance?.RefreshPlayer();
                Home.instance?.AddQueue();
                Queue.instance?.RefreshCurrent();
                ParseNextSong();
            }

            if (showPlayer)
            {
                MainActivity.instance.ShowPlayer();
            }
        }

        public static int CurrentID()
        {
            if (queue.Count <= currentID)
                currentID = -1;
            return currentID;
        }

        public static void SetSeekBar(SeekBar bar)
        {
            if (!UseCastPlayer)
            {
                bar.Max = (int)player.Duration;
                bar.Progress = (int)player.CurrentPosition;
                bar.ProgressChanged += (sender, e) =>
                {
                    int Progress = e.Progress;

                    if (player != null && player.Duration - Progress <= 1500 && player.Duration - Progress > 0)
                        ParseNextSong();
                };
                bar.StartTrackingTouch += (sender, e) =>
                {
                    autoUpdateSeekBar = false;
                };
                bar.StopTrackingTouch += (sender, e) =>
                {
                    autoUpdateSeekBar = true;
                    if (!queue[CurrentID()].IsLiveStream)
                        player.SeekTo(e.SeekBar.Progress);
                };
            }
            else
            {
                bar.Max = (int)RemotePlayer.StreamDuration;
                bar.Progress = (int)RemotePlayer.ApproximateStreamPosition;
                bar.StartTrackingTouch += (sender, e) =>
                {
                    autoUpdateSeekBar = false;
                };
                bar.StopTrackingTouch += (sender, e) =>
                {
                    autoUpdateSeekBar = true;
                    if (!queue[CurrentID()].IsLiveStream)
                        RemotePlayer.Seek(e.SeekBar.Progress);
                };
            }
        }

        void AddSongToDataBase(Song item)
        {
            Task.Run(() =>
            {
                SQLiteConnection db = new SQLiteConnection(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "Queue.sqlite"));
                db.CreateTable<Song>();

                db.InsertOrReplace(item);
            });
        }

        public static void UpdateQueueDataBase()
        {
            Task.Run(() =>
            {
                SQLiteConnection db = new SQLiteConnection(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "Queue.sqlite"));
                db.CreateTable<Song>();

                if (db.Table<Song>().Count() > queue.Count)
                {
                    db.DropTable<Song>();
                    db.CreateTable<Song>();
                }

                foreach (Song item in queue)
                {
                    db.InsertOrReplace(item);
                }
            });
        }

        void UpdateQueueItemDB(Song item)
        {
            Task.Run(() =>
            {
                SQLiteConnection db = new SQLiteConnection(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "Queue.sqlite"));
                db.CreateTable<Song>();

                db.InsertOrReplace(item);
            });
        }

        public static void SaveQueueSlot()
        {
            ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
            ISharedPreferencesEditor editor = pref.Edit();
            editor.PutInt("currentID", currentID == -1 ? 0 : currentID);
            editor.Apply();
        }

        public static void RetrieveQueueFromDataBase()
        {
            Task.Run(() =>
            {
                SQLiteConnection db = new SQLiteConnection(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "Queue.sqlite"));
                db.CreateTable<Song>();

                queue = db.Table<Song>().ToList().ConvertAll(RemoveParseValues);
                if (queue != null && queue.Count > 0)
                {
                    currentID = RetrieveQueueSlot();
                    LastTimer = RetrieveTimer();

                    MainActivity.instance?.RunOnUiThread(() => {
                        Home.instance?.AddQueue();
                        MainActivity.instance?.ShowSmallPlayer();
                    });
                }
                else
                {
                    MainActivity.instance?.RunOnUiThread(() => {
                        MainActivity.instance?.HideSmallPlayer();
                    });
                }
            });
        }

        static Song RemoveParseValues(Song song)
        {
            if (song.IsYt && song.IsParsed)
            {
                if (song.ExpireDate != null && song.ExpireDate.Value.Subtract(DateTimeOffset.UtcNow) > TimeSpan.Zero)
                {
                    return song;
                }
                else
                {
                    song.IsParsed = false;
                    song.Path = song.YoutubeID;
                    song.ExpireDate = null;
                }
            }
            return song;
        }

        public static int RetrieveQueueSlot()
        {
            ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
            int queueSlot = pref.GetInt("currentID", 0);
            return queueSlot == -1 ? 0 : queueSlot;
        }

        public static long RetrieveTimer()
        {
            ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
            return pref.GetLong("playerProgress", 0);
        }

        public static void SaveTimer(long currentProgress)
        {
            ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
            ISharedPreferencesEditor editor = pref.Edit();
            editor.PutLong("playerProgress", currentProgress);
            editor.Apply();
        }

        public static async void ParseNextSong()
        {
            if (CurrentID() == -1)
                return;

            if (CurrentID() + 1 > queue.Count - 1)
            {
                if(useAutoPlay)
                    instance.GenerateNext(1);
                return;
            }

            Song song = queue[CurrentID() + 1];
            if (song.IsParsed)
                return;

            if (!song.IsParsed && !parsing)
            {
                parsing = true;
                try
                {
                    YoutubeClient client = new YoutubeClient();
                    MediaStreamInfoSet mediaStreamInfo = await client.GetVideoMediaStreamInfosAsync(song.YoutubeID);
                    AudioStreamInfo streamInfo = mediaStreamInfo.Audio.Where(x => x.Container == Container.Mp4).OrderBy(s => s.Bitrate).Last();
                    song.IsParsed = true;
                    bool isLive = false;
                    string streamURL = streamInfo.Url;
                    if (mediaStreamInfo.HlsLiveStreamUrl != null)
                    {
                        streamURL = mediaStreamInfo.HlsLiveStreamUrl;
                        isLive = true;
                    }
                    song.Path = streamURL;

                    Video info = await client.GetVideoAsync(song.YoutubeID);
                    song.Album = info.Thumbnails.HighResUrl;
                    song.Artist = info.Author;

                    if(!isLive)
                    {
                        DateTimeOffset expireDate = mediaStreamInfo.ValidUntil;
                        song.ExpireDate = expireDate;
                    }

                    instance.UpdateQueueItemDB(song);
                    parsing = false;
                    if (Queue.instance != null)
                    {
                        int item = queue.IndexOf(song);
                        int firstItem = ((LinearLayoutManager)Queue.instance.ListView.GetLayoutManager()).FindFirstVisibleItemPosition();
                        int lastItem = ((LinearLayoutManager)Queue.instance.ListView.GetLayoutManager()).FindLastVisibleItemPosition();
                        if (firstItem < item && item < lastItem)
                        {
                            ImageView youtubeIcon = Queue.instance.ListView.GetChildAt(item - firstItem).FindViewById<ImageView>(Resource.Id.youtubeIcon);
                            youtubeIcon.SetImageResource(Resource.Drawable.PublicIcon);
                        }
                    }
                }
                catch (System.Net.Http.HttpRequestException)
                {
                    MainActivity.instance.Timout();
                    parsing = false;
                    return;
                }
                catch
                {
                    MainActivity.instance.Unknow();
                    parsing = false;
                    return;
                }
            }
        }

        public static long Duration
        {
            get
            {
                if(!UseCastPlayer)
                    return player == null ? 1 : player.Duration;
                else
                    return RemotePlayer == null ? 1 : RemotePlayer.StreamDuration;
            }
        }

        public static long CurrentPosition
        {
            get
            {
                if(!UseCastPlayer)
                    return player == null ? 0 : player.CurrentPosition;
                else
                    return RemotePlayer == null ? 1 : RemotePlayer.ApproximateStreamPosition;
            }
        }

        

        async void CreateNotification(string title, string artist, long albumArt = 0, string imageURI = "")
        {
            MusicPlayer.title = title;
            Bitmap icon = null;

            if(albumArt == -1)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        icon = Picasso.With(Application.Context).Load(imageURI).Error(Resource.Drawable.MusicIcon).Placeholder(Resource.Drawable.MusicIcon).Transform(new RemoveBlackBorder(true)).Get();
                    }
                    catch (System.Exception)
                    {
                        icon = Picasso.With(Application.Context).Load(Resource.Drawable.MusicIcon).Get();
                    }
                });
            }
            else
            {
                Uri songCover = Uri.Parse("content://media/external/audio/albumart");
                Uri iconURI = ContentUris.WithAppendedId(songCover, albumArt);

                await Task.Run(() =>
                {
                    try
                    {
                        icon = Picasso.With(Application.Context).Load(iconURI).Error(Resource.Drawable.MusicIcon).Placeholder(Resource.Drawable.MusicIcon).NetworkPolicy(NetworkPolicy.Offline).Resize(400, 400).CenterCrop().Get();
                    }
                    catch (System.Exception)
                    {
                        icon = Picasso.With(Application.Context).Load(Resource.Drawable.MusicIcon).Get();
                    }
                });
            }

            Intent tmpPreviousIntent = new Intent(Application.Context, typeof(MusicPlayer));
            tmpPreviousIntent.SetAction("Previus");
            PendingIntent previousIntent = PendingIntent.GetService(Application.Context, 0, tmpPreviousIntent, PendingIntentFlags.UpdateCurrent);

            Intent tmpPauseIntent = new Intent(Application.Context, typeof(MusicPlayer));
            tmpPauseIntent.SetAction("Pause");
            PendingIntent pauseIntent = PendingIntent.GetService(Application.Context, 0, tmpPauseIntent, PendingIntentFlags.UpdateCurrent);

            Intent tmpNextIntent = new Intent(Application.Context, typeof(MusicPlayer));
            tmpNextIntent.SetAction("Next");
            PendingIntent nextIntent = PendingIntent.GetService(Application.Context, 0, tmpNextIntent, PendingIntentFlags.UpdateCurrent);

            Intent tmpDefaultIntent = new Intent(Application.Context, typeof(MainActivity));
            tmpDefaultIntent.SetAction("Player");
            PendingIntent defaultIntent = PendingIntent.GetActivity(Application.Context, 0, tmpDefaultIntent, PendingIntentFlags.UpdateCurrent);

            Intent tmpDeleteIntent = new Intent(Application.Context, typeof(MusicPlayer));
            tmpDeleteIntent.SetAction("Stop");
            PendingIntent deleteIntent = PendingIntent.GetService(Application.Context, 0, tmpDeleteIntent, PendingIntentFlags.UpdateCurrent);

            notification = new NotificationCompat.Builder(Application.Context, "MusicApp.Channel")
                .SetVisibility(NotificationCompat.VisibilityPublic)
                .SetSmallIcon(Resource.Drawable.MusicIcon)

                .AddAction(Resource.Drawable.SkipPrevious, "Previous", previousIntent)
                .AddAction(Resource.Drawable.Pause, "Pause", pauseIntent)
                .AddAction(Resource.Drawable.SkipNext, "Next", nextIntent)

                .SetStyle(new MediaStyle()
                    .SetShowActionsInCompactView(1)
                    .SetShowCancelButton(true)
                    .SetMediaSession(mediaSession.SessionToken))
                .SetDeleteIntent(deleteIntent)
                .SetContentTitle(title)
                .SetContentText(artist)
                .SetLargeIcon(icon)
                .SetContentIntent(defaultIntent)
                .Build();
            ContextCompat.StartForegroundService(Application.Context, new Intent(Application.Context, typeof(MusicPlayer)));
            StartForeground(notificationID, notification);
        }

        public void Pause(bool userRequested)
        {
            if (userRequested)
                ShouldResumePlayback = false;

            if (!UseCastPlayer && player != null && isRunning)
            {
                SaveTimer(CurrentPosition);
                isRunning = false;

                Intent tmpPauseIntent = new Intent(Application.Context, typeof(MusicPlayer));
                tmpPauseIntent.SetAction("Pause");
                PendingIntent pauseIntent = PendingIntent.GetService(Application.Context, 0, tmpPauseIntent, PendingIntentFlags.UpdateCurrent);
                notification.Actions[1] = new Notification.Action(Resource.Drawable.Play, "Play", pauseIntent);
                notificationManager.Notify(notificationID, notification);

                player.PlayWhenReady = false;
                StopForeground(false);

                if (!ShouldResumePlayback && noisyRegistered)
                {
                    UnregisterReceiver(noisyReceiver);
                    noisyRegistered = false;
                }
            }
            else if(UseCastPlayer && RemotePlayer != null && isRunning)
            {
                isRunning = false;
                RemotePlayer.Pause();
            }

            SaveTimer(CurrentPosition);
            FrameLayout smallPlayer = MainActivity.instance.FindViewById<FrameLayout>(Resource.Id.smallPlayer);
            smallPlayer?.FindViewById<ImageButton>(Resource.Id.spPlay)?.SetImageResource(Resource.Drawable.Play);

            MainActivity.instance.FindViewById<ImageButton>(Resource.Id.playButton)?.SetImageResource(Resource.Drawable.Play);
            Queue.instance?.RefreshCurrent();
        }

        public void Resume()
        {
            if(!UseCastPlayer && player != null && !isRunning)
            {
                ISharedPreferences prefManager = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
                player.Volume = prefManager.GetInt("volumeMultiplier", 100) / 100f;
                isRunning = true;
                Intent tmpPauseIntent = new Intent(Application.Context, typeof(MusicPlayer));
                tmpPauseIntent.SetAction("Pause");
                PendingIntent pauseIntent = PendingIntent.GetService(Application.Context, 0, tmpPauseIntent, PendingIntentFlags.UpdateCurrent);

                notification.Actions[1] = new Notification.Action(Resource.Drawable.Pause, "Pause", pauseIntent);

                player.PlayWhenReady = true;
                StartForeground(notificationID, notification);

                if (noisyReceiver == null)
                    noisyReceiver = new AudioStopper();

                RegisterReceiver(noisyReceiver, new IntentFilter(AudioManager.ActionAudioBecomingNoisy));
                noisyRegistered = true;

                FrameLayout smallPlayer = MainActivity.instance.FindViewById<FrameLayout>(Resource.Id.smallPlayer);
                smallPlayer?.FindViewById<ImageButton>(Resource.Id.spPlay)?.SetImageResource(Resource.Drawable.Pause);

                if (Player.instance != null)
                {
                    MainActivity.instance?.FindViewById<ImageButton>(Resource.Id.playButton)?.SetImageResource(Resource.Drawable.Pause);
                    Player.instance.handler?.PostDelayed(Player.instance.UpdateSeekBar, 1000);
                }

                Queue.instance?.RefreshCurrent();

                AudioAttributes attributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)
                    .SetContentType(AudioContentType.Music)
                    .Build();

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    AudioFocusRequestClass focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                        .SetAudioAttributes(attributes)
                        .SetAcceptsDelayedFocusGain(true)
                        .SetWillPauseWhenDucked(true)
                        .SetOnAudioFocusChangeListener(this)
                        .Build();
                    AudioFocusRequest audioFocus = audioManager.RequestAudioFocus(focusRequest);

                    if (audioFocus != AudioFocusRequest.Granted)
                    {
                        Console.WriteLine("Can't Get Audio Focus");
                        return;
                    }
                }
                else
                {
#pragma warning disable CS0618 // Type or member is obsolete

                    AudioManager am = (AudioManager)MainActivity.instance.GetSystemService(AudioService);

                    AudioFocusRequest audioFocus = am.RequestAudioFocus(this, Stream.Music, AudioFocus.Gain);

                    if (audioFocus != AudioFocusRequest.Granted)
                    {
                        Console.WriteLine("Can't Get Audio Focus");
                        return;
                    }
#pragma warning restore CS0618
                }
            }
            else if(UseCastPlayer && RemotePlayer != null && player != null && !isRunning) //Maybe check that the session is initialised.
            {
                isRunning = true;
                RemotePlayer.Play();

                FrameLayout smallPlayer = MainActivity.instance.FindViewById<FrameLayout>(Resource.Id.smallPlayer);
                smallPlayer?.FindViewById<ImageButton>(Resource.Id.spPlay)?.SetImageResource(Resource.Drawable.Pause);

                if (Player.instance != null)
                {
                    MainActivity.instance?.FindViewById<ImageButton>(Resource.Id.playButton)?.SetImageResource(Resource.Drawable.Pause);
                    Player.instance.handler?.PostDelayed(Player.instance.UpdateSeekBar, 1000);
                }

                Queue.instance?.RefreshCurrent();
            }
            else
            {
                LastTimer = RetrieveTimer();
                SwitchQueue(CurrentID(), false, true);
            }
        }

        private MediaInfo GetMediaInfo(Song song)
        {
            MediaMetadata metadata = new MediaMetadata(MediaMetadata.MediaTypeMusicTrack);
            metadata.PutString(MediaMetadata.KeyTitle, song.Title);
            metadata.PutString(MediaMetadata.KeyArtist, song.Artist);
            metadata.AddImage(new Android.Gms.Common.Images.WebImage(Uri.Parse(song.Album), 1000, 1000));

            MediaInfo mediaInfo = new MediaInfo.Builder(song.Path)
                .SetStreamType(MediaInfo.StreamTypeBuffered)
                .SetContentType(MimeTypes.AudioMp4)
                .SetMetadata(metadata)
                .SetCustomData(new JSONObject(JsonConvert.SerializeObject(song)))
                .Build();

            return mediaInfo;
        }

        private MediaQueueItem GetQueueItem(Song song)
        {
            return new MediaQueueItem.Builder(GetMediaInfo(song)).Build();
        }

        public async static void GetQueueFromCast()
        {
            if (UseCastPlayer)
            {
                if (RemotePlayer?.MediaStatus?.QueueItems.Count == 0)
                {
                    Toast.MakeText(MainActivity.instance, "QueueItems count == 0", ToastLength.Long).Show();
                    return;
                }

                if(RemotePlayer.CurrentItem != null)
                    currentID = RemotePlayer.MediaQueue.IndexOfItemWithId(RemotePlayer.CurrentItem.ItemId);

                queue.Clear();
                for (int i = 0; i < RemotePlayer.MediaQueue.ItemCount; i++)
                    queue.Add((Song)RemotePlayer.MediaQueue.GetItemAtIndex(i, true));

                Console.WriteLine("&Waiting for fetch - queue count: " + queue.Count);

                if (queue.Count > 0)
                {
                    if (currentID != -1)
                    {
                        while (currentID >= queue.Count || queue[currentID] == null || Player.instance == null)
                            await Task.Delay(1000);
                    }

                    Console.WriteLine("&Fetched");

                    foreach (Song song in queue)
                        Console.WriteLine("&Song: " + song?.Title);

                    Intent intent = new Intent(MainActivity.instance, typeof(MusicPlayer));
                    intent.SetAction("CastListener");
                    MainActivity.instance.StartService(intent);

                    Home.instance?.AddQueue();
                    Home.instance?.RefreshQueue();
                    Queue.instance?.Refresh();
                    MainActivity.instance.ShowSmallPlayer();
                }
            }
        }


        public void Stop()
        {
            if (noisyRegistered)
                UnregisterReceiver(noisyReceiver);

            if(player != null && CurrentPosition != 0)
                SaveTimer(CurrentPosition);

            noisyRegistered = false;
            isRunning = false;
            title = null;
            parsing = false;
            currentID = -1;
            userStopped = false;
            MainActivity.instance?.HideSmallPlayer();
            if (player != null)
            {
                if (isRunning)
                    player.Stop();
                player.Release();
                player = null;
                StopForeground(true);
            }
            StopSelf();
        }

        private void SleepPause()
        {
            Stop();
        }

        public void OnAudioFocusChange(AudioFocus focusChange)
        {
            Console.WriteLine("&AudioFocus Changed: " + focusChange.ToString());
            ISharedPreferences prefManager = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
            switch (focusChange)
            {
                case AudioFocus.Gain:
                    if (ShouldResumePlayback)
                    {
                        if (player == null)
                            InitializeService();

                        Resume();
                    }

                    if (player != null)
                        player.Volume = prefManager.GetInt("volumeMultiplier", 100) / 100f;
                    volumeDuked = false;
                    break;

                case AudioFocus.Loss:
                    Pause(false);
                    ShouldResumePlayback = false;
                    break;

                case AudioFocus.LossTransient:
                    Pause(false);
                    ShouldResumePlayback = true;
                    break;

                case AudioFocus.LossTransientCanDuck:
                    volumeDuked = true;
                    player.Volume = prefManager.GetInt("volumeMultiplier", 100) / 500f;
                    ShouldResumePlayback = true;
                    break;

                default:
                    break;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            Stop();
            instance = null;
        }

        public void OnLoadingChanged(bool p0) { }

        public void OnPlaybackParametersChanged(PlaybackParameters p0) { }

        public void OnPlayerError(ExoPlaybackException args)
        {
            Console.WriteLine("&Type: " + args.Type + " : " +  args.Cause + " : " + args.Data);
            Player.instance?.Error();


            Intent tmpErrorIntent = new Intent(Application.Context, typeof(MusicPlayer));
            tmpErrorIntent.SetAction("ForceResume");
            PendingIntent errorIntent = PendingIntent.GetService(Application.Context, 0, tmpErrorIntent, PendingIntentFlags.UpdateCurrent);

            notification.Actions[1] = new Notification.Action(Resource.Drawable.Error, "Error", errorIntent);
            notificationManager.Notify(notificationID, notification);
        }

        public void OnPlayerStateChanged(bool playWhenReady, int state)
        {
            if (state == Com.Google.Android.Exoplayer2.Player.StateEnded)
            {
                PlayNext();
            }
            if(state == Com.Google.Android.Exoplayer2.Player.StateBuffering)
            {
                Player.instance?.Buffering();
            }
            if(state == Com.Google.Android.Exoplayer2.Player.StateReady)
            {
                Player.instance?.Ready();
            }
        }


        public void OnPositionDiscontinuity() { }

        public void OnRepeatModeChanged(int p0) { }

        public void OnTracksChanged(TrackGroupArray p0, TrackSelectionArray p1) { }

        public void OnPositionDiscontinuity(int p0) { }

        public void OnSeekProcessed() { }

        public void OnShuffleModeEnabledChanged(bool p0) { }

        public void OnTimelineChanged(Timeline p0, Java.Lang.Object p1, int p2) { }
    }
}
 