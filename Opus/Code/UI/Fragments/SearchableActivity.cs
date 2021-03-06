﻿using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using Newtonsoft.Json;
using Opus.Adapter;
using Opus.DataStructure;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AlertDialog = Android.Support.V7.App.AlertDialog;
using SearchView = Android.Support.V7.Widget.SearchView;
using Toolbar = Android.Support.V7.Widget.Toolbar;

namespace Opus.Fragments
{
    [Activity(Label = "SearchableActivity", Theme = "@style/Theme", ParentActivity = typeof(MainActivity), WindowSoftInputMode = SoftInput.StateAlwaysVisible)]
    public class SearchableActivity : AppCompatActivity, IMenuItemOnActionExpandListener
    {
        public static SearchableActivity instance;
        public SearchView searchView;
        public string SearchQuery = null;
        private ListView ListView;
        private SuggestionAdapter adapter;
        private List<Suggestion> History = new List<Suggestion>();
        private List<Suggestion> suggestions = new List<Suggestion>();

        protected async override void OnCreate(Bundle savedInstanceState)
        {
            instance = this;
            base.OnCreate(savedInstanceState);

            MainActivity.LoadTheme(this);
            SetContentView(Resource.Layout.SearchLayout);

            SetSupportActionBar(FindViewById<Toolbar>(Resource.Id.toolbar));
            SupportActionBar.Title = "";
            ListView = FindViewById<ListView>(Resource.Id.searchSuggestions);

            ListView.Divider = null;
            ListView.DividerHeight = 0;
            ListView.ItemClick += (sender, e) =>
            {
                searchView.SetQuery(suggestions[e.Position].Text, true);
            };
            ListView.ItemLongClick += (sender, e) =>
            {
                Suggestion toRemove = suggestions[e.Position];
                if (History.Contains(toRemove))
                {
                    AlertDialog dialog = new AlertDialog.Builder(this, MainActivity.dialogTheme)
                        .SetTitle(suggestions[e.Position].Text)
                        .SetMessage(Resource.String.remove_search)
                        .SetPositiveButton(Resource.String.remove, (send, eventArg) =>
                        {
                            History.Remove(toRemove);
                            adapter.Remove(toRemove);
                            suggestions.Remove(toRemove);
                            Task.Run(() =>
                            {
                                SQLiteConnection db = new SQLiteConnection(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "RecentSearch.sqlite"));
                                db.Delete(db.Table<Suggestion>().ToList().Find(x => x.Text == toRemove.Text));
                            });
                        })
                        .SetNegativeButton(Resource.String.cancel, (send, eventArg) => { })
                        .Create();
                    dialog.Show();
                }
            };

            await Task.Run(() => 
            {
                SQLiteConnection db = new SQLiteConnection(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "RecentSearch.sqlite"));
                db.CreateTable<Suggestion>();

                History = db.Table<Suggestion>().ToList().ConvertAll(HistoryItem);
                History.Reverse();
                suggestions = History;
            });

            adapter = new SuggestionAdapter(instance, Resource.Layout.SuggestionLayout, suggestions);
            ListView.Adapter = adapter;
        }

        Suggestion HistoryItem(Suggestion suggestion)
        {
            return new Suggestion(Resource.Drawable.History, suggestion.Text);
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.search_toolbar, menu);
            IMenuItem searchItem = menu.FindItem(Resource.Id.search);
            searchItem.ExpandActionView();
            searchView = searchItem.ActionView.JavaCast<SearchView>();
            searchView.MaxWidth = int.MaxValue;
            searchView.QueryHint = GetString(Resource.String.youtube_search);
            searchView.SetQuery(((SearchView)MainActivity.instance.menu.FindItem(Resource.Id.search).ActionView).Query, false);
            searchView.QueryTextChange += (s, e) =>
            {
                Console.WriteLine("&Search view query changed, newText: " + e.NewText);
                if (e.NewText.Length > 0)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            using (WebClient client = new WebClient { Encoding = System.Text.Encoding.UTF7 })
                            {
                                string json = client.DownloadString("http://suggestqueries.google.com/complete/search?client=youtube&ds=yt&client=firefox&q=" + /*WebUtility.HtmlEncode(*/e.NewText/*)*/);
                                json = json.Substring(json.IndexOf(",") + 1);
                                json = json.Remove(json.Length - 1);
                                List<string> items = JsonConvert.DeserializeObject<List<string>>(json);
                                suggestions = items.ConvertAll(StringToSugest);
                                suggestions.InsertRange(0, History.Where(x => x.Text.StartsWith(e.NewText)));

                                if (SearchQuery == null || SearchQuery == "")
                                    RunOnUiThread(new Java.Lang.Runnable(() => { ListView.Adapter = new SuggestionAdapter(instance, Resource.Layout.SuggestionLayout, suggestions); }));
                            }
                        }
                        catch(Exception ex) { Console.WriteLine("&Search ex: " + ex.Message); }
                    });
                }
                else
                {
                    suggestions = History;
                    adapter = new SuggestionAdapter(instance, Resource.Layout.SuggestionLayout, suggestions);
                    ListView.Adapter = adapter;
                }
            };
            searchView.QueryTextSubmit += (s, e) =>
            {
                SearchQuery = e.NewText;
                AddQueryToHistory(e.NewText);
                Finish();
                OverridePendingTransition(0, 0);
                e.Handled = true;
            };
            searchItem.SetOnActionExpandListener(this);
            return base.OnCreateOptionsMenu(menu);
        }

        void AddQueryToHistory(string query)
        {
            if (!History.ConvertAll(SuggestToQuery).Contains(query, new QueryComparer()))
            {
                Task.Run(() =>
                {
                    SQLiteConnection db = new SQLiteConnection(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "RecentSearch.sqlite"));
                    db.CreateTable<Suggestion>();

                    db.Insert(new Suggestion(-1, query));
                });
            }
            else
            {
                Task.Run(() =>
                {
                    SQLiteConnection db = new SQLiteConnection(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "RecentSearch.sqlite"));
                    db.CreateTable<Suggestion>();

                    db.Delete(db.Table<Suggestion>().ToList().Find(x => x.Text == query));
                    db.Insert(new Suggestion(-1, query));
                });
            }
        }

        Suggestion StringToSugest(string text)
        {
            return new Suggestion(Android.Resource.Drawable.IcSearchCategoryDefault, text);
        }

        string SuggestToQuery(Suggestion suggestion)
        {
            return suggestion.Text;
        }

        public void Refine(int position)
        {
            searchView.SetQuery(suggestions[position].Text, false);
        }

        protected override void OnStop()
        {
            base.OnStop();
            if ((SearchQuery == null || SearchQuery == "") && YoutubeSearch.instances == null)
                MainActivity.instance.CancelSearch();
        }

        public override void OnBackPressed()
        {
            Finish();
            OverridePendingTransition(0, 0);
        }

        public bool OnMenuItemActionCollapse(IMenuItem item)
        {
            Finish();
            OverridePendingTransition(0, 0);
            return false;
        }

        public bool OnMenuItemActionExpand(IMenuItem item) { return true; }
    }

    public class QueryComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y)
        {
            return x.Trim().ToLower().Equals(y.Trim().ToLower());
        }

        public int GetHashCode(string obj)
        {
            return obj.GetHashCode();
        }
    }
}