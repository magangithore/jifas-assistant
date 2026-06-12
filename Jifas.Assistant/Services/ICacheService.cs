using System;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Kontrak cache aplikasi.
    /// Implementasinya bisa memakai memory cache lokal atau distributed cache seperti Redis.
    /// </summary>
    public interface ICacheService
    {
        /// <summary>
        /// Ambil value dari cache.
        /// </summary>
        /// <typeparam name="T">Tipe data yang disimpan.</typeparam>
        /// <param name="key">Key cache.</param>
        /// <returns>Value cache, atau default jika tidak ditemukan.</returns>
        T Get<T>(string key);

        /// <summary>
        /// Simpan value ke cache dengan masa berlaku tertentu.
        /// </summary>
        /// <typeparam name="T">Tipe data yang disimpan.</typeparam>
        /// <param name="key">Key cache.</param>
        /// <param name="value">Value yang akan disimpan.</param>
        /// <param name="durationMinutes">Durasi cache dalam menit.</param>
        void Set<T>(string key, T value, int durationMinutes);

        /// <summary>
        /// Hapus satu item cache.
        /// </summary>
        /// <param name="key">Key cache yang akan dihapus.</param>
        void Remove(string key);

        /// <summary>
        /// Bersihkan seluruh cache jika provider mendukung.
        /// </summary>
        void Clear();

        /// <summary>
        /// Cek apakah key tersedia di cache.
        /// </summary>
        /// <param name="key">Key cache.</param>
        /// <returns>True jika key ditemukan.</returns>
        bool Exists(string key);
    }
}
