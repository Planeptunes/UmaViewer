using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // SweatLocator track (sweatLocatorList): drives the sweat drops hung on each character's head
    // (the "sweatlocator" prefab: sweat_01..sweat_05, one drop model each). Field names, order and types
    // follow LiveTimelineKeySweatLocatorData of the JP client (owner +0x30, alpha +0x34,
    // randomVisibleCount +0x38, then 5 x {isVisible, offset, offsetAngle, offsetType}).
    [Serializable]
    public class LiveTimelineKeySweatLocatorData : LiveTimelineKeyWithInterpolate
    {
        public const int LocatorCount = 5;

        public enum LocatorType
        {
            Local = 0, // offset is the drop's local position (under its sweat_0N node)
            World = 1  // offset is added to the world position of its sweat_0N node
        }

        // Plain data copy of one locator (client struct LocatorInfo: isVisible +0x00, offset +0x04,
        // offsetAngle +0x10, offsetType +0x1C, size 0x20).
        public struct LocatorInfo
        {
            public bool isVisible;
            public Vector3 offset;
            public Vector3 offsetAngle;
            public LocatorType offsetType;
        }

        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.SweatLocator;

        public int owner;
        public float alpha;
        public int randomVisibleCount;

        public bool locator0_isVisible;
        public Vector3 locator0_offset;
        public Vector3 locator0_offsetAngle;
        public LocatorType locator0_offsetType;

        public bool locator1_isVisible;
        public Vector3 locator1_offset;
        public Vector3 locator1_offsetAngle;
        public LocatorType locator1_offsetType;

        public bool locator2_isVisible;
        public Vector3 locator2_offset;
        public Vector3 locator2_offsetAngle;
        public LocatorType locator2_offsetType;

        public bool locator3_isVisible;
        public Vector3 locator3_offset;
        public Vector3 locator3_offsetAngle;
        public LocatorType locator3_offsetType;

        public bool locator4_isVisible;
        public Vector3 locator4_offset;
        public Vector3 locator4_offsetAngle;
        public LocatorType locator4_offsetType;

        public bool GetIsVisible(int index)
        {
            switch (index)
            {
                case 0: return locator0_isVisible;
                case 1: return locator1_isVisible;
                case 2: return locator2_isVisible;
                case 3: return locator3_isVisible;
                default: return locator4_isVisible;
            }
        }

        public Vector3 GetOffset(int index)
        {
            switch (index)
            {
                case 0: return locator0_offset;
                case 1: return locator1_offset;
                case 2: return locator2_offset;
                case 3: return locator3_offset;
                default: return locator4_offset;
            }
        }

        public Vector3 GetOffsetAngle(int index)
        {
            switch (index)
            {
                case 0: return locator0_offsetAngle;
                case 1: return locator1_offsetAngle;
                case 2: return locator2_offsetAngle;
                case 3: return locator3_offsetAngle;
                default: return locator4_offsetAngle;
            }
        }

        public LocatorType GetOffsetType(int index)
        {
            switch (index)
            {
                case 0: return locator0_offsetType;
                case 1: return locator1_offsetType;
                case 2: return locator2_offsetType;
                case 3: return locator3_offsetType;
                default: return locator4_offsetType;
            }
        }
    }

    [Serializable]
    public class LiveTimelineKeySweatLocatorDataList
        : LiveTimelineKeyDataListTemplate<LiveTimelineKeySweatLocatorData>
    {
    }

    [Serializable]
    public class LiveTimelineSweatLocatorData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "SweatLocator";

        public LiveTimelineKeySweatLocatorDataList keys;

        // Runtime state of the random-visibility subset (indices of the locators that stay visible while a key
        // has randomVisibleCount > 0). The client keeps it in the group object between frames.
        public List<int> randomVisibleIndex = new List<int>();

        public override ILiveTimelineKeyDataList GetKeyList()
        {
            return keys;
        }

        public LiveTimelineSweatLocatorData()
            : base(default_name)
        {
            keys = new LiveTimelineKeySweatLocatorDataList();
        }
    }

    // Client class SweatLocatorUpdateInfo: owner (character index), alpha and one LocatorInfo per locator.
    // The same instance is reused for every group (the client keeps a single object in LiveTimelineControl).
    public class SweatLocatorUpdateInfo
    {
        public int owner;
        public float alpha;
        public LiveTimelineKeySweatLocatorData.LocatorInfo[] locatorInfo;

        public SweatLocatorUpdateInfo(int locatorNum)
        {
            locatorInfo = new LiveTimelineKeySweatLocatorData.LocatorInfo[locatorNum];
        }
    }

    public delegate void SweatLocatorUpdateInfoDelegate(SweatLocatorUpdateInfo updateInfo);
}
