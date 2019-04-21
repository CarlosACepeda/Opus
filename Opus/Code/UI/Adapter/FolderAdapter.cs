﻿using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Opus.DataStructure;
using Opus.Fragments;
using System.Collections.Generic;

namespace Opus.Adapter
{
    public class FolderAdapter : ArrayAdapter
    {
        public int selectedPosition;

        private Context context;
        private List<Folder> folders;
        private LayoutInflater inflater;
        private int resource;


        public FolderAdapter(Context context, int resource, List<Folder> folders) : base(context, resource, folders)
        {
            this.context = context;
            this.resource = resource;
            this.folders = folders;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            if (inflater == null)
            {
                inflater = (LayoutInflater)context.GetSystemService(Context.LayoutInflaterService);
            }
            if (convertView == null)
            {
                convertView = inflater.Inflate(resource, parent, false);
            }
            FolderHolder holder = new FolderHolder(convertView)
            {
                Name = { Text = folders[position].name },
            };

            holder.expandChild.Visibility = ViewStates.Visible;

            if (!folders[position].asChild)
                holder.expandChild.Visibility = ViewStates.Invisible;

            if(folders[position].isExtended)
                holder.expandChild.SetImageResource(Resource.Drawable.ExpandLess);
            else
                holder.expandChild.SetImageResource(Resource.Drawable.ArrowDown);

            convertView.FindViewById<RelativeLayout>(Resource.Id.folderList).SetPadding(folders[position].Padding, 0, 0, 0);

            holder.used.SetTag(Resource.Id.folderUsed, folders[position].uri);
            holder.used.Click += DownloadFragment.instance.Used_Click;
            holder.used.Checked = position == selectedPosition;
            holder.used.SetTag(Resource.Id.folderName, position);

            if(MainActivity.Theme == 1)
            {
                holder.Name.SetTextColor(Color.White);
                holder.expandChild.SetColorFilter(Color.White);
                holder.used.ButtonTintList = ColorStateList.ValueOf(Color.White);
            }
            else
            {
                convertView.SetBackgroundColor(Color.White);
            }

            return convertView;
        }
    }
}