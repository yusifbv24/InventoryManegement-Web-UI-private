﻿namespace SharedServices.Services
{
    public static class SearchHelper
    {
        public static string NormalizeForSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Convert to lowercase first
            text = text.ToLowerInvariant();

            // Replace Azerbaijani special characters with their ASCII equivalents
            var replacements = new Dictionary<char, char>
            {
                {'ə', 'e'}, {'Ə', 'e'},
                {'ğ', 'g'}, {'Ğ', 'g'},
                {'ü', 'u'}, {'Ü', 'u'},
                {'ş', 's'}, {'Ş', 's'},
                {'ı', 'i'}, {'İ', 'i'}, {'I', 'i'},
                {'ö', 'o'}, {'Ö', 'o'},
                {'ç', 'c'}, {'Ç', 'c'}
            };

            var result = new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                result[i] = replacements.TryGetValue(text[i], out var replacement)
                    ? replacement
                    : char.ToLowerInvariant(text[i]);
            }

            return new string(result);
        }

        public static bool ContainsAzerbaijani(string? text, string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(searchTerm))
                return false;

            var normalizedText = NormalizeForSearch(text);
            var normalizedSearch = NormalizeForSearch(searchTerm);

            return normalizedText.Contains(normalizedSearch);
        }
    }
}