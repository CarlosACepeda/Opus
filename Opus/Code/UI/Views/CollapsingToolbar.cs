﻿using Android.Content;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Util;
using Opus.Fragments;
using System;

[Register("Opus/CollapsingToolbarLayout")]
public class CollapsingToolbar : CollapsingToolbarLayout
{
    public CollapsingToolbar(Context context) : base(context) { }
    public CollapsingToolbar(Context context, IAttributeSet attrs) : base(context, attrs) { }
    public CollapsingToolbar(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) { }
    protected CollapsingToolbar(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        if ((PlaylistTracks.instance != null && PlaylistTracks.instance.useHeader) ||ChannelDetails.instance != null)
            heightMeasureSpec = widthMeasureSpec;


        base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
    }
}