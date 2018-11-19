﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Java.Lang;

namespace MusicApp.Resources.Portable_Class
{
    public class PlaylistLocationAdapter : ArrayAdapter
    {
        public bool YoutubeWorkflow;

        public PlaylistLocationAdapter(Context context, int resource) : base(context, resource) { }

        public PlaylistLocationAdapter(Context context, int resource, int textViewResourceId) : base(context, resource, textViewResourceId) { }

        public PlaylistLocationAdapter(Context context, int resource, IList objects) : base(context, resource, objects) { }

        public PlaylistLocationAdapter(Context context, int resource, Java.Lang.Object[] objects) : base(context, resource, objects) { }

        public PlaylistLocationAdapter(Context context, int resource, int textViewResourceId, IList objects) : base(context, resource, textViewResourceId, objects) { }

        public PlaylistLocationAdapter(Context context, int resource, int textViewResourceId, Java.Lang.Object[] objects) : base(context, resource, textViewResourceId, objects) { }

        protected PlaylistLocationAdapter(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

        public override bool AreAllItemsEnabled()
        {
            return false;
        }

        public override bool IsEnabled(int position)
        {
            if (position == 0)
                return true;
            else if (YoutubeWorkflow)
                return true;
            else
                return false;
        }
    }
}