﻿using Android.App;
using Android.Content;
using Android.Database;
using Android.Gms.Auth.Api;
using Android.Gms.Auth.Api.SignIn;
using Android.OS;
using Android.Preferences;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Android.Provider.MediaStore.Audio;
using AlertDialog = Android.Support.V7.App.AlertDialog;
using Toolbar = Android.Support.V7.Widget.Toolbar;

namespace MusicApp.Resources.Portable_Class
{
    [Activity(Label = "Settings", Theme = "@style/Theme")]
    public class Preferences : PreferenceActivity
    {
        public static Preferences instance;
        public Toolbar toolbar;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            if(MainActivity.Theme == 1)
                SetTheme(Resource.Style.DarkPreferences);

            instance = this;

            FragmentManager.BeginTransaction().Replace(Android.Resource.Id.Content, new PreferencesFragment()).Commit();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            instance = null;
        }

        protected override void OnPostCreate(Bundle savedInstanceState)
        {
            base.OnPostCreate(savedInstanceState);
            LinearLayout root = (LinearLayout)FindViewById(Android.Resource.Id.List).Parent.Parent.Parent;
            toolbar = (Toolbar)LayoutInflater.From(this).Inflate(Resource.Layout.PreferenceToolbar, root, false);
            root.AddView(toolbar, 0);
            toolbar.Title = "Settings";
            toolbar.NavigationClick += (sender, e) => 
            {
                if(DownloadFragment.instance == null && TopicSelector.instance == null)
                    Finish();
                else
                {
                    if(DownloadFragment.instance != null)
                    {
                        ISharedPreferences prefManager = PreferenceManager.GetDefaultSharedPreferences(this);
                        ISharedPreferencesEditor editor = prefManager.Edit();
                        editor.PutString("downloadPath", DownloadFragment.instance.path);
                        editor.Apply();
                        DownloadFragment.instance = null;
                        Recreate();
                    }
                    else if(TopicSelector.instance != null)
                    {
                        ISharedPreferences prefManager = PreferenceManager.GetDefaultSharedPreferences(this);
                        ISharedPreferencesEditor editor = prefManager.Edit();
                        List<string> topics = new List<string>();
                        for (int i = 0; i < TopicSelector.instance.selectedTopics.Count; i++)
                        {
                            topics.Add(TopicSelector.instance.selectedTopics[i] + "/#-#/" + TopicSelector.instance.selectedTopicsID[i]);
                        }
                        editor.PutStringSet("selectedTopics", topics);
                        editor.Apply();
                        TopicSelector.instance = null;
                        Recreate();
                    }
                }
            };
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode == 5981)
            {
                GoogleSignInResult result = Auth.GoogleSignInApi.GetSignInResultFromIntent(data);
                if (result.IsSuccess)
                {
                    MainActivity.account = result.SignInAccount;
                    PreferencesFragment.instance?.SignedIn();
                }
            }
        }
    }

    public class PreferencesFragment : PreferenceFragment
    {
        public static PreferencesFragment instance;
        private View view;

        //Local Shortcut
        private int LSposition;

        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            AskForPermission();

            instance = this;
            AddPreferencesFromResource(Resource.Layout.Preferences);
            ISharedPreferences prefManager = PreferenceManager.GetDefaultSharedPreferences(Application.Context);

            //Music Genres
            Preference topicPreference = PreferenceScreen.FindPreference("topics");
            topicPreference.PreferenceClick += TopicPreference;
            string[] topics = prefManager.GetStringSet("selectedTopics", new string[] { }).ToArray();

            if (topics.Length == 0)
                topicPreference.Summary = "Actually nothing";
            else if (topics.Length == 1)
                topicPreference.Summary = topics[0].Substring(0, topics[0].IndexOf("/#-#/"));
            else if (topics.Length == 2)
                topicPreference.Summary = topics[0].Substring(0, topics[0].IndexOf("/#-#/")) + " and " + topics[1].Substring(0, topics[1].IndexOf("/#-#/"));
            else if(topics.Length == 3)
                topicPreference.Summary = topics[0].Substring(0, topics[0].IndexOf("/#-#/")) + ", " + topics[1].Substring(0, topics[1].IndexOf("/#-#/")) + " and " + topics[2].Substring(0, topics[2].IndexOf("/#-#/"));
            else if(topics.Length > 3)
                topicPreference.Summary = topics[0].Substring(0, topics[0].IndexOf("/#-#/")) + ", " + topics[1].Substring(0, topics[1].IndexOf("/#-#/")) + ", " + topics[2].Substring(0, topics[2].IndexOf("/#-#/")) + " and more.";

            //Download Path
            Preference downloadPref = PreferenceScreen.FindPreference("downloadPath");
            downloadPref.PreferenceClick += DownloadClick;
            downloadPref.Summary = prefManager.GetString("downloadPath", "not set");

            //Skip Exist Verification
            Preference skipExistVerification = PreferenceScreen.FindPreference("skipExistVerification");
            skipExistVerification.PreferenceClick += SkipClick;
            skipExistVerification.Summary = prefManager.GetBoolean("skipExistVerification", false) ? "True" : "False";

            //Local play shortcut
            Preference localShortcutPreference = PreferenceScreen.FindPreference("localPlay");
            localShortcutPreference.PreferenceClick += LocalShortcut;
            localShortcutPreference.Summary = prefManager.GetString("localPlay", "Shuffle All Audio Files");

            //Theme
            Preference themePreference = PreferenceScreen.FindPreference("theme");
            themePreference.PreferenceClick += ChangeTheme;
            themePreference.Summary = prefManager.GetInt("theme", 0) == 0 ? "White Theme" : "Dark Theme";

            //Check For Update
            Preference updatePreference = PreferenceScreen.FindPreference("update");
            updatePreference.PreferenceClick += UpdatePreference_PreferenceClick;

            //Account
            Preference accountPreference = PreferenceScreen.FindPreference("account");

            if (MainActivity.account != null)
            {
                accountPreference.Title = "Logged in as:";
                accountPreference.Summary = MainActivity.account.DisplayName;
            }
        }

        private async void AskForPermission()
        {
            await Task.Delay(100);
            MainActivity.instance.GetStoragePermission();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            instance = null;
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            view = base.OnCreateView(inflater, container, savedInstanceState);
            view.SetPadding(0, MainActivity.instance.SupportActionBar.Height, 0, 0);
            return view;
        }

        public void SignedIn()
        {
            AccountPreference accountPreference = (AccountPreference)PreferenceScreen.FindPreference("account");
            accountPreference.Title = "Logged in as:";
            accountPreference.Summary = MainActivity.account.DisplayName;
            accountPreference.OnSignedIn();
        }

        #region Topic Preference
        private void TopicPreference(object sender, Preference.PreferenceClickEventArgs e)
        {
            FragmentManager.BeginTransaction().Replace(Android.Resource.Id.ListContainer, TopicSelector.NewInstance()).AddToBackStack(null).Commit();
            instance = null;
            Preferences.instance.toolbar.Title = "Music Genres";
        }
        #endregion

        #region Download location
        private void DownloadClick(object sender, Preference.PreferenceClickEventArgs e)
        {
            FragmentManager.BeginTransaction().Replace(Android.Resource.Id.ListContainer, DownloadFragment.NewInstance()).AddToBackStack(null).Commit();
            instance = null;
            Preferences.instance.toolbar.Title = "Download Location";
        }
        #endregion

        #region Skip Verification
        private void SkipClick(object sender, Preference.PreferenceClickEventArgs e)
        {
            AlertDialog.Builder builder = new AlertDialog.Builder(Activity, MainActivity.dialogTheme);
            builder.SetTitle("Always play youtube file even if you have already downloaded the track:");
            builder.SetItems(new[] { "True", "False" }, (s, args) =>
            {
                ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
                ISharedPreferencesEditor editor = pref.Edit();
                editor.PutBoolean("skipExistVerification", args.Which == 0);
                editor.Apply();

                Preference prefButton = FindPreference("skipExistVerification");
                prefButton.Summary = args.Which == 0 ? "True" : "False";
            });
            builder.Show();
        }
        #endregion

        #region LocalShortcut
        private void LocalShortcut(object sender, Preference.PreferenceClickEventArgs e)
        {
            string[] items = new string[] { "Shuffle All Audio Files", "Shuffle a playlist" };

            AlertDialog.Builder builder = new AlertDialog.Builder(Activity, MainActivity.dialogTheme);
            builder.SetTitle("Set the local storage shortcut:");
            builder.SetItems(items, (s, args) => { if (args.Which == 0) LCShuffleAll(); else LCSufflePlaylist(); });
            builder.Show();
        }

        void LCShuffleAll()
        {
            ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
            ISharedPreferencesEditor editor = pref.Edit();
            editor.PutString("localPlay", "Shuffle All Audio Files");
            editor.Apply();

            Preference prefButton = FindPreference("localPlay");
            prefButton.Summary = "Shuffle All Audio Files";
        }

        void LCSufflePlaylist()
        {
            List<string> playList = new List<string>();
            List<long> playlistId = new List<long>();

            Android.Net.Uri uri = Playlists.ExternalContentUri;
            CursorLoader loader = new CursorLoader(Application.Context, uri, null, null, null, null);
            ICursor cursor = (ICursor)loader.LoadInBackground();

            if (cursor != null && cursor.MoveToFirst())
            {
                int nameID = cursor.GetColumnIndex(Playlists.InterfaceConsts.Name);
                int listID = cursor.GetColumnIndex(Playlists.InterfaceConsts.Id);
                do
                {
                    string name = cursor.GetString(nameID);
                    long id = cursor.GetLong(listID);
                    playList.Add(name);
                    playlistId.Add(id);

                }
                while (cursor.MoveToNext());
                cursor.Close();
            }

            AlertDialog.Builder builder = new AlertDialog.Builder(Activity, MainActivity.dialogTheme);
            builder.SetTitle("Set the local storage shortcut:");
            builder.SetSingleChoiceItems(playList.ToArray(), -1, (s, args) => { LSposition = args.Which; });
            builder.SetPositiveButton("Ok", (s, args) => { LCSufflePlaylist(playList[LSposition], playlistId[LSposition]); });
            builder.SetNegativeButton("Cancel", (s, args) => { return; });
            builder.Show();
        }

        void LCSufflePlaylist(string playlist, long playlistID)
        {
            ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
            ISharedPreferencesEditor editor = pref.Edit();
            editor.PutString("localPlay", "Shuffle " + playlist);
            editor.PutLong("localPlaylistID", playlistID);
            editor.Apply();

            Preference prefButton = FindPreference("localPlay");
            prefButton.Summary = "Shuffle " + playlist;
        }
        #endregion

        #region Theme
        private void ChangeTheme(object sender, Preference.PreferenceClickEventArgs e)
        {
            AlertDialog.Builder builder = new AlertDialog.Builder(Activity, MainActivity.dialogTheme);
            builder.SetTitle("Choose a theme :");
            builder.SetItems(new[] { "White Theme", "Dark Theme" }, (s, args) =>
            {
                ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(Application.Context);
                ISharedPreferencesEditor editor = pref.Edit();
                editor.PutInt("theme", args.Which);
                editor.Apply();

                Preference prefButton = FindPreference("theme");
                prefButton.Summary = args.Which == 0 ? "White Theme" : "Dark Theme";

                MainActivity.instance.SwitchTheme(args.Which);
                MainActivity.instance.Recreate();
                //MainActivity.Theme = args.Which;
                //MainActivity.dialogTheme = args.Which == 0 ? Resource.Style.AppCompatAlertDialogStyle : Resource.Style.AppCompatDarkAlertDialogStyle;
                Activity.Recreate();
            });
            builder.Show();
        }
        #endregion

        #region Updater
        private void UpdatePreference_PreferenceClick(object sender, Preference.PreferenceClickEventArgs e)
        {
            MainActivity.CheckForUpdate();
        }
        #endregion
    }
}