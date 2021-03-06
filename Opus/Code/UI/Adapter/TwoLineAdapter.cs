﻿using Android.Graphics;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Widget;
using Opus.DataStructure;
using Opus.Fragments;
using System;
using System.Collections.Generic;

namespace Opus.Adapter
{
    public class TwoLineAdapter : RecyclerView.Adapter
    {
        private List<string> Title;
        private readonly List<int> Count;
        public EventHandler<int> ItemClick;
        public EventHandler<int> ItemLongClick;

        public TwoLineAdapter(List<string> Title, List<int> Count)
        {
            this.Title = Title;
            this.Count = Count;
        }

        public override int ItemCount => Title.Count;

        public override void OnBindViewHolder(RecyclerView.ViewHolder viewHolder, int position)
        {
            TwoLineHolder holder = (TwoLineHolder)viewHolder;
            holder.Line1.Text = Title[position];
            holder.Line2.Text = Count[position].ToString() + " " + (Title.Count < 2 ? MainActivity.instance.GetString(Resource.String.element) : MainActivity.instance.GetString(Resource.String.elements));

            if (!holder.more.HasOnClickListeners)
            {
                holder.more.Tag = position;
                holder.more.Click += (sender, e) =>
                {
                    int pos = (int)((ImageView)sender).Tag;

                    if (FolderBrowse.instance != null)
                        FolderBrowse.instance.More(pos);
                };
            }
        }

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            View view = LayoutInflater.From(parent.Context).Inflate(Resource.Layout.TwoLineLayout, parent, false);
            return new TwoLineHolder(view, OnClick, OnLongClick);
        }

        void OnClick(int position)
        {
            ItemClick?.Invoke(this, position);
        }

        void OnLongClick(int position)
        {
            ItemLongClick?.Invoke(this, position);
        }
    }
}