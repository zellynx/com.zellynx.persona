using System;

namespace Character.Mobility
{
    public readonly struct CharacterCollisionEventArgs
    {
        public CharacterHit Hit { get; }

        public CharacterCollisionEventArgs(CharacterHit hit)
        {
            Hit = hit;
        }
    }
}
