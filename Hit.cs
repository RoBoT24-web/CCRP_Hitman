using System;

namespace Hitman
{
    public class Hit
    {
        public int Id { get; set; }
        public ulong TargetId { get; set; }
        public string TargetName { get; set; }
        public ulong HirerId { get; set; }
        public string HirerName { get; set; }
        public decimal Bounty { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
