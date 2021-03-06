﻿using Android.Animation;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Gms.Cast.Framework;
using Android.Graphics;
using Android.OS;
using Android.Renderscripts;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V4.Widget;
using Android.Support.V7.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Util;
using Android.Views;
using Android.Widget;
using Opus.Adapter;
using Opus.Api;
using Opus.Api.Services;
using Opus.DataStructure;
using Opus.Others;
using Square.Picasso;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaRouteButton = Android.Support.V7.App.MediaRouteButton;

namespace Opus
{
    [Register("Opus/Player")]
    [Activity(Label = "Player", Theme = "@style/Theme", ScreenOrientation = ScreenOrientation.Portrait, LaunchMode = LaunchMode.SingleTop)]
    public class Player : Android.Support.V4.App.Fragment, Palette.IPaletteAsyncListener
    {
        public static Player instance;
        public Handler handler = new Handler();
        public static bool errorState = false;
        public bool? playNext = true;

        private SeekBar bar;
        private ProgressBar spBar;
        private TextView timer;
        private ImageView albumArt;
        public DrawerLayout DrawerLayout;
        private bool prepared = false;
        private readonly int[] timers = new int[] { 0, 2, 10, 30, 60, 120 };
        private int checkedItem = 0;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            instance = this;
            View view = inflater.Inflate(Resource.Layout.player, container, false);
            if (!view.FindViewById<ImageButton>(Resource.Id.playButton).HasOnClickListeners)
            {
                view.FindViewById<ImageButton>(Resource.Id.downButton).Click += Down_Click;
                view.FindViewById<ImageButton>(Resource.Id.showQueue).Click += ShowQueue_Click;
                view.FindViewById<ImageButton>(Resource.Id.lastButton).Click += Last_Click;
                view.FindViewById<ImageButton>(Resource.Id.playButton).Click += Play_Click;
                view.FindViewById<ImageButton>(Resource.Id.nextButton).Click += Next_Click;
                view.FindViewById<ImageButton>(Resource.Id.moreButton).Click += More;
                view.FindViewById<ImageButton>(Resource.Id.repeat).Click += (sender, e) => { MusicPlayer.Repeat(); };
                view.FindViewById<ImageButton>(Resource.Id.fav).Click += Fav;
            }

            albumArt = view.FindViewById<ImageView>(Resource.Id.playerAlbum);
            timer = view.FindViewById<TextView>(Resource.Id.timer);
            bar = view.FindViewById<SeekBar>(Resource.Id.songTimer);
            bar.ProgressChanged += async (sender, e) =>
            {
                if (!MusicPlayer.isLiveStream)
                    timer.Text = string.Format("{0} | {1}", DurationToTimer(e.Progress), DurationToTimer(await MusicPlayer.Duration()));
            };
            bar.StartTrackingTouch += (sender, e) =>
            {
                MusicPlayer.autoUpdateSeekBar = false;
            };
            bar.StopTrackingTouch += async (sender, e) =>
            {
                MusicPlayer.autoUpdateSeekBar = true;
                if (!(await MusicPlayer.GetItem()).IsLiveStream)
                    MusicPlayer.SeekTo(e.SeekBar.Progress);
            };

            DrawerLayout = view.FindViewById<DrawerLayout>(Resource.Id.queueDrawer);
            DrawerLayout.AddDrawerListener(new QueueListener(view.FindViewById<ImageView>(Resource.Id.queueBackground)));

            DisplayMetrics metrics = new DisplayMetrics();
            Activity.WindowManager.DefaultDisplay.GetMetrics(metrics);
            view.FindViewById(Resource.Id.queueParent).LayoutParameters.Width = (int)(metrics.WidthPixels * 0.75f);
            ((FrameLayout.LayoutParams)view.FindViewById(Resource.Id.queue).LayoutParameters).TopMargin = Resources.GetDimensionPixelSize(Resources.GetIdentifier("status_bar_height", "dimen", "android"));

            spBar = Activity.FindViewById<ProgressBar>(Resource.Id.spProgress);
            CastButtonFactory.SetUpMediaRouteButton(Activity, view.FindViewById<MediaRouteButton>(Resource.Id.castButton));
            RefreshPlayer();
            return view;
        }


        public async void RefreshPlayer()
        {
            while (MainActivity.instance == null || MusicPlayer.CurrentID() == -1)
                await Task.Delay(100);

            Song current = await MusicPlayer.GetItem();

            if (current == null || (current.IsYt && current.Album == null))
                return;

            FrameLayout smallPlayer = MainActivity.instance.FindViewById<FrameLayout>(Resource.Id.smallPlayer);
            smallPlayer.FindViewById<TextView>(Resource.Id.spTitle).Text = current.Title;
            smallPlayer.FindViewById<TextView>(Resource.Id.spArtist).Text = current.Artist;
            smallPlayer.FindViewById<ImageView>(Resource.Id.spPlay).SetImageResource(Resource.Drawable.Pause);
            ImageView art = smallPlayer.FindViewById<ImageView>(Resource.Id.spArt);

            if (!current.IsYt)
            {
                var songCover = Android.Net.Uri.Parse("content://media/external/audio/albumart");
                var nextAlbumArtUri = ContentUris.WithAppendedId(songCover, current.AlbumArt);

                Picasso.With(Application.Context).Load(nextAlbumArtUri).Placeholder(Resource.Drawable.noAlbum).Resize(400, 400).CenterCrop().Into(art);
            }
            else
            {
                Picasso.With(Application.Context).Load(current.Album).Placeholder(Resource.Drawable.noAlbum).Transform(new RemoveBlackBorder(true)).Into(art);
            }

            TextView title = MainActivity.instance.FindViewById<TextView>(Resource.Id.playerTitle);
            TextView artist = MainActivity.instance.FindViewById<TextView>(Resource.Id.playerArtist);
            albumArt = MainActivity.instance.FindViewById<ImageView>(Resource.Id.playerAlbum);
            SpannableString titleText = new SpannableString(current.Title);
            titleText.SetSpan(new BackgroundColorSpan(Color.ParseColor("#BF000000")), 0, current.Title.Length, SpanTypes.InclusiveInclusive);
            title.TextFormatted = titleText;
            SpannableString artistText = new SpannableString(current.Artist);
            artistText.SetSpan(new BackgroundColorSpan(Color.ParseColor("#BF000000")), 0, current.Artist.Length, SpanTypes.InclusiveInclusive);
            artist.TextFormatted = artistText;

            if (!errorState)
            {
                if (MusicPlayer.isRunning)
                {
                    MainActivity.instance.FindViewById<ImageButton>(Resource.Id.playButton).SetImageResource(Resource.Drawable.Pause);
                    smallPlayer.FindViewById<ImageButton>(Resource.Id.spPlay).SetImageResource(Resource.Drawable.Pause);
                }
                else
                {
                    MainActivity.instance.FindViewById<ImageButton>(Resource.Id.playButton).SetImageResource(Resource.Drawable.Play);
                    smallPlayer.FindViewById<ImageButton>(Resource.Id.spPlay).SetImageResource(Resource.Drawable.Play);
                }
            }

            Bitmap drawable = null;
            if (current.AlbumArt == -1)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        drawable = Picasso.With(Application.Context).Load(current.Album).Error(Resource.Drawable.noAlbum).Placeholder(Resource.Drawable.noAlbum).Transform(new RemoveBlackBorder(true)).Get();
                    }
                    catch
                    {
                        drawable = Picasso.With(Application.Context).Load(Resource.Drawable.noAlbum).Get();
                    }
                });
            }
            else
            {
                Android.Net.Uri songCover = Android.Net.Uri.Parse("content://media/external/audio/albumart");
                Android.Net.Uri iconURI = ContentUris.WithAppendedId(songCover, current.AlbumArt);

                await Task.Run(() =>
                {
                    try
                    {
                        drawable = Picasso.With(Application.Context).Load(iconURI).Error(Resource.Drawable.noAlbum).Placeholder(Resource.Drawable.noAlbum).NetworkPolicy(NetworkPolicy.Offline).Get();
                    }
                    catch
                    {
                        drawable = Picasso.With(Application.Context).Load(Resource.Drawable.noAlbum).Get();
                    }
                });
            }

            albumArt.SetImageBitmap(drawable);
            Palette.From(drawable).MaximumColorCount(28).Generate(this);

            if (await SongManager.IsFavorite(current))
                MainActivity.instance?.FindViewById<ImageButton>(Resource.Id.fav)?.SetImageResource(Resource.Drawable.Unfav);
            else
                MainActivity.instance?.FindViewById<ImageButton>(Resource.Id.fav)?.SetImageResource(Resource.Drawable.Fav);


            if (albumArt.Width > 0)
            {
                try
                {
                    //k is the coeficient to convert ImageView's size to Bitmap's size
                    //We want to take the lower coeficient because if we take the higher, we will have a final bitmap larger than the initial one and we can't create pixels
                    float k = Math.Min((float)drawable.Height / albumArt.Height, (float)drawable.Width / albumArt.Width); 
                    int width = (int)(albumArt.Width * k);
                    int height = (int)(albumArt.Height * k);

                    int dX = (int)((drawable.Width - width) * 0.5f);
                    int dY = (int)((drawable.Height - height) * 0.5f);

                    Console.WriteLine("&Drawable Info: Width: " + drawable.Width + " Height: " + drawable.Height);
                    Console.WriteLine("&AlbumArt Info: Width: " + albumArt.Width + " Height: " + albumArt.Height);
                    Console.WriteLine("&Blur Creation: Width: " + width + " Height: " + height + " dX: " + dX + " dY: " + dY);
                    //The width of the view in pixel (we'll multiply this by 0.75f because the drawer has a width of 75%)
                    Bitmap blured = Bitmap.CreateBitmap(drawable, dX, dY, (int)(width * 0.75f), height);
                    Console.WriteLine("&BLured bitmap created");

                    RenderScript rs = RenderScript.Create(MainActivity.instance);
                    Allocation input = Allocation.CreateFromBitmap(rs, blured);
                    Allocation output = Allocation.CreateTyped(rs, input.Type);
                    ScriptIntrinsicBlur blurrer = ScriptIntrinsicBlur.Create(rs, Element.U8_4(rs));
                    blurrer.SetRadius(13);
                    blurrer.SetInput(input);
                    blurrer.ForEach(output);

                    output.CopyTo(blured);
                    MainActivity.instance.FindViewById<ImageView>(Resource.Id.queueBackground).SetImageBitmap(blured);
                    Console.WriteLine("&Bitmap set to image view");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("&Queue background error: " + ex.Message);
                }
            }

            if (bar != null)
            {
                if (spBar == null)
                    spBar = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.spProgress);

                if (current.IsLiveStream)
                {
                    bar.Max = 1;
                    bar.Progress = 1;
                    spBar.Max = 1;
                    spBar.Progress = 1;
                    timer.Text = "🔴 LIVE";
                }
                else
                {
                    int duration = await MusicPlayer.Duration();
                    bar.Max = duration;
                    timer.Text = string.Format("{0} | {1}", DurationToTimer((int)MusicPlayer.CurrentPosition), DurationToTimer(duration));
                    spBar.Max = duration;
                    spBar.Progress = (int)MusicPlayer.CurrentPosition;

                    handler.PostDelayed(UpdateSeekBar, 1000);

                    int LoadedMax = (int)await MusicPlayer.LoadDuration();
                    bar.Max = LoadedMax;
                    spBar.Max = LoadedMax;
                    timer.Text = string.Format("{0} | {1}", DurationToTimer((int)MusicPlayer.CurrentPosition), DurationToTimer(LoadedMax));
                }
            }
        }

        public void Buffering()
        {
            ImageButton play = MainActivity.instance.FindViewById<ImageButton>(Resource.Id.playButton);
            ProgressBar buffer = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.playerBuffer);
            buffer.Visibility = ViewStates.Visible;
            buffer.IndeterminateTintList = ColorStateList.ValueOf(Color.White);
            buffer.SetY(play.GetY());
            play.Visibility = ViewStates.Gone;

            MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.spBuffer).Visibility = ViewStates.Visible;
            MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spPlay).Visibility = ViewStates.Invisible;
        }

        public void Error()
        {
            ImageButton play = MainActivity.instance.FindViewById<ImageButton>(Resource.Id.playButton);
            ProgressBar buffer = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.playerBuffer);
            buffer.Visibility = ViewStates.Gone;
            play.Visibility = ViewStates.Visible;
            play.SetImageResource(Resource.Drawable.Error);

            ProgressBar smallBuffer = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.spBuffer);
            ImageButton smallPlay = MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spPlay);
            smallBuffer.Visibility = ViewStates.Gone;
            smallPlay.Visibility = ViewStates.Visible;
            smallPlay.SetImageResource(Resource.Drawable.Error);

            errorState = true;
        }

        public void Ready()
        {
            ImageButton play = MainActivity.instance.FindViewById<ImageButton>(Resource.Id.playButton);
            ProgressBar buffer = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.playerBuffer);
            if (buffer == null || play == null)
                return;
            buffer.Visibility = ViewStates.Gone;
            play.Visibility = ViewStates.Visible;
            if(MusicPlayer.isRunning)
                play.SetImageResource(Resource.Drawable.Pause);
            else
                play.SetImageResource(Resource.Drawable.Play);

            errorState = false;
            ProgressBar smallBuffer = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.spBuffer);
            ImageButton smallPlay = MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spPlay);
            smallBuffer.Visibility = ViewStates.Gone;
            smallPlay.Visibility = ViewStates.Visible;

        }

        public void Stoped()
        {
            MainActivity.instance.ShowSmallPlayer();
            MainActivity.instance.SheetBehavior.State = BottomSheetBehavior.StateCollapsed;
        }

        public async void UpdateSeekBar()
        {
            if (!MusicPlayer.isRunning)
            {
                handler.RemoveCallbacks(UpdateSeekBar);
                return;
            }
            if(MusicPlayer.autoUpdateSeekBar)
            {
                bar.Progress = (int)MusicPlayer.CurrentPosition;
                timer.Text = string.Format("{0} | {1}", DurationToTimer((int)MusicPlayer.CurrentPosition), DurationToTimer(await MusicPlayer.Duration()));
            }
            spBar.Progress = (int)MusicPlayer.CurrentPosition;
            handler.PostDelayed(UpdateSeekBar, 100);
        }

        private string DurationToTimer(int duration)
        {
            int hours = duration / 3600000;
            int minutes = duration / 60000 % 60;
            int seconds = duration / 1000 % 60;

            string hour = hours.ToString();
            string min = minutes.ToString();
            string sec = seconds.ToString();
            if (hour.Length == 1)
                hour = "0" + hour;
            if (min.Length == 1)
                min = "0" + min;
            if (sec.Length == 1)
                sec = "0" + sec;

            return (hours == 0) ? (min + ":" + sec) : (hour + ":" + min + ":" + sec);
        }

        private void Down_Click(object sender, EventArgs e)
        {
            MainActivity.instance.ShowSmallPlayer();
            MainActivity.instance.SheetBehavior.State = BottomSheetBehavior.StateCollapsed;
        }

        private void ShowQueue_Click(object sender, EventArgs e)
        {
            ShowQueue();
        }

        public void ShowQueue()
        {
            DrawerLayout.OpenDrawer((int)GravityFlags.Start);
        }

        private void Last_Click(object sender, EventArgs e)
        {
            Intent intent = new Intent(MainActivity.instance, typeof(MusicPlayer));
            intent.SetAction("Previus");
            MainActivity.instance.StartService(intent);
        }

        private void Play_Click(object sender, EventArgs e)
        {
            if (errorState)
            {
                MusicPlayer.instance?.Resume();
                errorState = false;
                return;
            }

            Intent intent = new Intent(MainActivity.instance, typeof(MusicPlayer));
            intent.SetAction("Pause");
            MainActivity.instance.StartService(intent);
        }

        private void Next_Click(object sender, EventArgs e)
        {
            Intent intent = new Intent(MainActivity.instance, typeof(MusicPlayer));
            intent.SetAction("Next");
            MainActivity.instance.StartService(intent);
        }

        public void Repeat(bool repeat)
        {
            if(repeat)
                MainActivity.instance.FindViewById<ImageButton>(Resource.Id.repeat)?.SetColorFilter(Color.Argb(255, 21, 183, 237), PorterDuff.Mode.Multiply);
            else
                MainActivity.instance?.FindViewById<ImageButton>(Resource.Id.repeat)?.ClearColorFilter();
        }

        private async void Fav(object sender, EventArgs e)
        {
            Song current = await MusicPlayer.GetItem();
            if (await SongManager.IsFavorite(current))
            {
                MainActivity.instance?.FindViewById<ImageButton>(Resource.Id.fav)?.SetImageResource(Resource.Drawable.Fav);
                SongManager.UnFav(current);
            }
            else
            {
                MainActivity.instance?.FindViewById<ImageButton>(Resource.Id.fav)?.SetImageResource(Resource.Drawable.Unfav);
                SongManager.Fav(current);
            }
        }

        private async void More(object s, EventArgs e)
        {
            Song item = await MusicPlayer.GetItem();

            BottomSheetDialog bottomSheet = new BottomSheetDialog(MainActivity.instance);
            View bottomView = MainActivity.instance.LayoutInflater.Inflate(Resource.Layout.BottomSheet, null);
            bottomView.FindViewById<TextView>(Resource.Id.bsTitle).Text = item.Title;
            bottomView.FindViewById<TextView>(Resource.Id.bsArtist).Text = item.Artist;
            if (item.AlbumArt == -1 || item.IsYt)
            {
                Picasso.With(MainActivity.instance).Load(item.Album).Placeholder(Resource.Drawable.noAlbum).Transform(new RemoveBlackBorder(true)).Into(bottomView.FindViewById<ImageView>(Resource.Id.bsArt));
            }
            else
            {
                var songCover = Android.Net.Uri.Parse("content://media/external/audio/albumart");
                var songAlbumArtUri = ContentUris.WithAppendedId(songCover, item.AlbumArt);

                Picasso.With(MainActivity.instance).Load(songAlbumArtUri).Placeholder(Resource.Drawable.noAlbum).Resize(400, 400).CenterCrop().Into(bottomView.FindViewById<ImageView>(Resource.Id.bsArt));
            }
            bottomSheet.SetContentView(bottomView);

            List<BottomSheetAction> actions = new List<BottomSheetAction>
            {
                new BottomSheetAction(Resource.Drawable.Timer, Resources.GetString(Resource.String.timer), (sender, eventArg) => { SleepDialog(); bottomSheet.Dismiss(); }),
                new BottomSheetAction(Resource.Drawable.PlaylistAdd, Resources.GetString(Resource.String.add_to_playlist), (sender, eventArg) => { PlaylistManager.AddSongToPlaylistDialog(item); bottomSheet.Dismiss(); })
            };

            if (item.IsYt)
            {
                actions.AddRange(new BottomSheetAction[]
                {
                    new BottomSheetAction(Resource.Drawable.PlayCircle, Resources.GetString(Resource.String.create_mix_from_song), (sender, eventArg) =>
                    {
                        YoutubeManager.CreateMixFromSong(item);
                        bottomSheet.Dismiss();
                    }),
                    new BottomSheetAction(Resource.Drawable.Download, Resources.GetString(Resource.String.download), (sender, eventArg) =>
                    {
                        Console.WriteLine("&Trying to download " + item.Title);
                        YoutubeManager.Download(new[]{ item });
                        bottomSheet.Dismiss();
                    }),
                    new BottomSheetAction(Resource.Drawable.OpenInBrowser, Resources.GetString(Resource.String.open_youtube), (sender, eventArg) =>
                    {
                        Intent intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse("vnd.youtube://" + MusicPlayer.queue[MusicPlayer.CurrentID()].YoutubeID));
                        StartActivity(intent);
                        bottomSheet.Dismiss();
                    })
                });

                if (item.ChannelID != null && item.ChannelID != "")
                {
                    actions.Add(new BottomSheetAction(Resource.Drawable.account, Resources.GetString(Resource.String.goto_channel), (sender, eventArg) =>
                    {
                        ChannelManager.OpenChannelTab(item.ChannelID);
                        bottomSheet.Dismiss();
                    }));
                }
            }
            else
            {
                actions.Add(new BottomSheetAction(Resource.Drawable.Edit, Resources.GetString(Resource.String.edit_metadata), (sender, eventArg) =>
                {
                    LocalManager.EditMetadata(item, MusicPlayer.CurrentID());
                    bottomSheet.Dismiss();
                }));
            }

            bottomSheet.FindViewById<ListView>(Resource.Id.bsItems).Adapter = new BottomSheetAdapter(MainActivity.instance, Resource.Layout.BottomSheetText, actions);
            bottomSheet.Show();
        }

        public void SleepDialog()
        {
            string minutes = GetString(Resource.String.minutes);
            Android.Support.V7.App.AlertDialog.Builder builder = new Android.Support.V7.App.AlertDialog.Builder(MainActivity.instance, MainActivity.dialogTheme);
            builder.SetTitle(Resource.String.sleep_timer);
            builder.SetSingleChoiceItems(new string[] { GetString(Resource.String.off), "2 " + minutes, "10 " + minutes, "30 " + minutes, "1 " + GetString(Resource.String.hour), "2 " + GetString(Resource.String.hours) }, checkedItem, ((senders, eventargs) => { checkedItem = eventargs.Which; }));
            builder.SetPositiveButton(Resource.String.ok, ((senders, args) => { Sleep(timers[checkedItem]); }));
            builder.SetNegativeButton(Resource.String.cancel, ((senders, args) => { }));
            builder.Show();
        }

        void Sleep(int time)
        {
            Intent intent = new Intent(MainActivity.instance, typeof(Sleeper));
            intent.PutExtra("time", time);
            MainActivity.instance.StartService(intent);
        }

        public void OnGenerated(Palette palette)
        {
            if (MainActivity.instance == null || IsDetached)
                return;

            List<Palette.Swatch> swatches = palette.Swatches.OrderBy(x => x.Population).ToList();
            int i = swatches.Count - 1;
            Palette.Swatch swatch = palette.MutedSwatch;

            if (swatch == null && swatches.Count == 0)
                return;

            while (swatch == null)
            {
                swatch = swatches[i];
                i--;

                if (i == -1 && swatch == null)
                    return;
            }

            Palette.Swatch accent = null;
            if (IsColorDark(swatch.Rgb))
            {
                accent = palette.LightVibrantSwatch;

                if (accent == null)
                    accent = palette.LightMutedSwatch;

                if (accent == null)
                    accent = swatch;
            }
            else
            {
                accent = palette.DarkVibrantSwatch;

                if (accent == null)
                    accent = palette.DarkMutedSwatch;

                if (accent == null)
                    accent = swatch;
            }

            Color text = Color.Argb(Color.GetAlphaComponent(swatch.BodyTextColor), Color.GetRedComponent(swatch.BodyTextColor), Color.GetGreenComponent(swatch.BodyTextColor), Color.GetBlueComponent(swatch.BodyTextColor));
            Color background = Color.Argb(Color.GetAlphaComponent(swatch.Rgb), Color.GetRedComponent(swatch.Rgb), Color.GetGreenComponent(swatch.Rgb), Color.GetBlueComponent(swatch.Rgb));
            Color accentColor = Color.Argb(Color.GetAlphaComponent(accent.Rgb), Color.GetRedComponent(accent.Rgb), Color.GetGreenComponent(accent.Rgb), Color.GetBlueComponent(accent.Rgb));
            MainActivity.instance.FindViewById<TextView>(Resource.Id.spTitle).SetTextColor(text);
            MainActivity.instance.FindViewById<TextView>(Resource.Id.spArtist).SetTextColor(text);

            //Reveal for the smallPlayer
            if (prepared)
            {
                View spReveal = MainActivity.instance.FindViewById<View>(Resource.Id.spReveal);
                if (spReveal != null && spReveal.IsAttachedToWindow)
                {
                    Animator spAnim = ViewAnimationUtils.CreateCircularReveal(spReveal, playNext == false ? spReveal.Width : 0, spReveal.Height / 2, 0, spReveal.Width);
                    spAnim.AnimationStart += (sender, e) => { spReveal.SetBackgroundColor(background); };
                    spAnim.AnimationEnd += (sender, e) => { MainActivity.instance.FindViewById(Resource.Id.playersHolder).SetBackgroundColor(background); };
                    spAnim.SetDuration(500);
                    spAnim.StartDelay = 10;
                    spAnim.Start();
                }
            }
            else
            {
                prepared = true;
                MainActivity.instance.FindViewById(Resource.Id.playersHolder).SetBackgroundColor(background);
            }
            playNext = null;

            if (bar == null)
                bar = MainActivity.instance.FindViewById<SeekBar>(Resource.Id.songTimer);

            if (spBar == null)
                spBar = MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.spProgress);

            bar.ProgressTintList = ColorStateList.ValueOf(accentColor);
            bar.ThumbTintList = ColorStateList.ValueOf(accentColor);
            bar.ProgressBackgroundTintList = ColorStateList.ValueOf(Color.Argb(87, accentColor.R, accentColor.G, accentColor.B));
            spBar.ProgressTintList = ColorStateList.ValueOf(accentColor);
            spBar.ProgressBackgroundTintList = ColorStateList.ValueOf(Color.Argb(87, accentColor.R, accentColor.G, accentColor.B));

            if (IsColorDark(accent.Rgb))
            {
                MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spNext).ImageTintList = ColorStateList.ValueOf(Color.White);
                MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spPlay).ImageTintList = ColorStateList.ValueOf(Color.White);
                MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spLast).ImageTintList = ColorStateList.ValueOf(Color.White);
                MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.spBuffer).IndeterminateTintList = ColorStateList.ValueOf(Color.White);
            }
            else
            {
                MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spNext).ImageTintList = ColorStateList.ValueOf(Color.Black);
                MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spPlay).ImageTintList = ColorStateList.ValueOf(Color.Black);
                MainActivity.instance.FindViewById<ImageButton>(Resource.Id.spLast).ImageTintList = ColorStateList.ValueOf(Color.Black);
                MainActivity.instance.FindViewById<ProgressBar>(Resource.Id.spBuffer).IndeterminateTintList = ColorStateList.ValueOf(Color.Black);
            }
        }

        public bool IsColorDark(int color)
        {
            double darkness = 1 - (0.299 * Color.GetRedComponent(color) + 0.587 * Color.GetGreenComponent(color) + 0.114 * Color.GetBlueComponent(color)) / 255;
            if (darkness < 0.7)
            {
                return false; // It's a light color
            }
            else
            {
                return true; // It's a dark color
            }
        }
    }

    public class PlayerCallback : BottomSheetBehavior.BottomSheetCallback
    {
        private readonly Activity context;
        private readonly NestedScrollView sheet;
        private readonly BottomNavigationView bottomView;
        private readonly FrameLayout smallPlayer;
        private readonly View playerContainer;
        private readonly CoordinatorLayout snackBar;
        private bool Refreshed = false;
        private SheetMovement movement = SheetMovement.Unknow;

        public PlayerCallback(Activity context)
        {
            this.context = context;
            sheet = context.FindViewById<NestedScrollView>(Resource.Id.playerSheet);
            bottomView = context.FindViewById<BottomNavigationView>(Resource.Id.bottomView);
            smallPlayer = context.FindViewById<FrameLayout>(Resource.Id.smallPlayer);
            playerContainer = context.FindViewById(Resource.Id.playerContainer);
            snackBar = context.FindViewById<CoordinatorLayout>(Resource.Id.snackBar);
        }

        public override void OnSlide(View bottomSheet, float slideOffset)
        {
            smallPlayer.Visibility = ViewStates.Visible;

            if (movement == SheetMovement.Unknow)
            {
                if (slideOffset > 0)
                    movement = SheetMovement.Expanding;
                else if (slideOffset < 0)
                    movement = SheetMovement.Hidding;
            }

            if(movement == SheetMovement.Expanding && 0 <= slideOffset && slideOffset <= 1)
            {
                sheet.Alpha = 1;
                bottomView.TranslationY = (int)((56 * context.Resources.DisplayMetrics.Density + 0.5f) * slideOffset);
                sheet.TranslationY = -(int)((56 * context.Resources.DisplayMetrics.Density + 0.5f) * (1 - slideOffset));

                playerContainer.Alpha = Math.Max(0, (slideOffset - 0.5f) * 2.5f);
                smallPlayer.Alpha = Math.Max(0, 1 - slideOffset * 2);
                snackBar.TranslationY = (int)((50 * context.Resources.DisplayMetrics.Density + 0.5f) * slideOffset);

                if (!Refreshed && slideOffset > .3)
                {
                    Refreshed = true;
                    Player.instance.RefreshPlayer();
                }
                else if (slideOffset < .3)
                    Refreshed = false;
            }
            else if(movement == SheetMovement.Hidding && - 1 <= slideOffset && slideOffset < 0)
            {
                sheet.Alpha = 1 + slideOffset;
                MusicPlayer.instance?.ChangeVolume(MusicPlayer.instance.volume * (1 + slideOffset));
            }
        }

        public override void OnStateChanged(View bottomSheet, int newState)
        {
            if (newState == BottomSheetBehavior.StateExpanded)
            {
                sheet.Alpha = 1;
                playerContainer.Alpha = 1;
                smallPlayer.Alpha = 0;
                smallPlayer.Visibility = ViewStates.Gone;
                bottomSheet.TranslationY = (int)(56 * context.Resources.DisplayMetrics.Density + 0.5f);
                sheet.TranslationY = 0;
                snackBar.TranslationY = (int)(50 * context.Resources.DisplayMetrics.Density + 0.5f);
                movement = SheetMovement.Unknow;
            }
            else if (newState == BottomSheetBehavior.StateCollapsed)
                movement = SheetMovement.Unknow;
            else if(newState == BottomSheetBehavior.StateHidden)
            {
                movement = SheetMovement.Unknow;
                if (!MainActivity.instance.SkipStop)
                {
                    Intent intent = new Intent(context, typeof(MusicPlayer));
                    intent.SetAction("Stop");
                    intent.PutExtra("saveQueue", false);
                    context.StartService(intent);
                }
                MainActivity.instance.SkipStop = false;
                sheet.Alpha = 1;
                MusicPlayer.instance?.ChangeVolume(MusicPlayer.instance.volume);
            }
        }
    }

    public enum SheetMovement { Expanding, Hidding, Unknow }


    public class QueueListener : Java.Lang.Object, DrawerLayout.IDrawerListener
    {
        private readonly ImageView QueueBackground;

        public QueueListener(ImageView queueBackground)
        {
            QueueBackground = queueBackground;
        }

        public void OnDrawerOpened(View drawerView)
        {
            MainActivity.instance.SheetBehavior.PreventSlide = true;
        }

        public void OnDrawerClosed(View drawerView)
        {
            MainActivity.instance.SheetBehavior.PreventSlide = false;
        }

        public void OnDrawerSlide(View drawerView, float slideOffset)
        {
            QueueBackground.TranslationX = (1 - slideOffset) * drawerView.Width;
        }

        public void OnDrawerStateChanged(int newState)
        {
            if (newState == DrawerLayout.StateDragging)
                FixedNestedScrollView.PreventSlide = true;
            else if(newState == DrawerLayout.StateSettling)
                FixedNestedScrollView.PreventSlide = false;
        }
    }
}