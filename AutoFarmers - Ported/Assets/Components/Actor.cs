﻿using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[Serializable]
public struct Actor : IComponentData
{
    public float2 targetPosition;
    
    
}
