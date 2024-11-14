using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace SmtpTelegramRelay.Common
{
    internal static class InputMediaHelper
    {
        private static readonly Dictionary<string, InputMediaType> MediaTypes = new(StringComparer.InvariantCultureIgnoreCase)
        {
            { "jpg", InputMediaType.Photo },
            { "jpeg", InputMediaType.Photo },
            { "png", InputMediaType.Photo },
            { "webp", InputMediaType.Photo },
            { "svg", InputMediaType.Photo },
            { "tiff", InputMediaType.Photo },
            { "gif", InputMediaType.Photo }
        };


        public static InputMediaType GetMediaType(string extension)
        {
            if (!MediaTypes.TryGetValue(extension.TrimStart('.'), out var mediaType))
                mediaType = InputMediaType.Document;
            return mediaType;
        }

        public static IAlbumInputMedia GetInputMedia(InputMediaType mediatype, InputFileStream stream) =>
            mediatype switch
            {
                InputMediaType.Photo => new InputMediaPhoto(stream),
                _ => new InputMediaDocument(stream)
            };

        public static IAlbumInputMedia GetInputMedia(string extension, InputFileStream stream) 
            => GetInputMedia(GetMediaType(extension), stream);
    }
}
