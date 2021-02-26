﻿using System.Threading.Tasks;

namespace RockPaperScissorsGame.Client.Services
{
    public interface ISingleStorage<T>
    {
        public void Update(T item);
        Task UpdateAsync(T item);
        public T Get();
    }
}
