using System;

namespace Character.Mobility
{
    [Flags]
    public enum CharacterBodyCollisionFlags
    {
        None = 0,
        Sides = 1 << 0,
        Above = 1 << 1,
        Below = 1 << 2,
    }
}
