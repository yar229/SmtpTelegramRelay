# What is SmtpTelegramRelay

SmtpTelegramRelay is an SMTP server that relays all received emails to specified telegram bot subscribers. Runs as a windows service or as a standalone application. Fully written in C#.

# Setup

1. Create `appsettings.develop.json` (copy from `appsettings.json` example) and put your real values there. At least specify a telegram bot token and a chat ID.
   - `appsettings.json` is only an example and contains no secrets.
   - The application loads `appsettings.{environment}.json`, where environment comes from `ASPNETCORE_ENVIRONMENT` (defaults to `Production`, set `Develop` for local development).
   - You can specify `ASPNETCORE_ENVIRONMENT` in a `.env` file next to the executable (copy from `.env.example`, it is loaded by the `DotNetEnv` library). Both `appsettings.develop.json`, `appsettings.production.json` and `.env` are in `.gitignore`.
```json
{
  // The port that the relay will listen on to receive SMTP e-mail messages, the default is 25.
  // No authorization is required when connecting to this port, select Basic Authorization if it is required
  "SmtpPort": 25,
  // Interface to bind the SMTP listener to, use "*" to listen on all interfaces
  "SmtpAddress": "*",
  // Port for the HTTP API (see the HTTP API section), the default is 80
  "HttpPort": 5026,
  // Interface to bind the HTTP listener to, use "*" to listen on all interfaces
  "HttpAddress": "*",
  // Route Telegram requests through a proxy if you cannot reach Telegram directly
  "UseProxy": false,
  "Proxy": "socks5://user:password@proxy.example.com:1080",
  // Your token for the Telegram bot, get it at https://t.me/BotFather when registering the bot
  "TelegramBotToken": "SPECIFY THERE TELEGRAM BOT TOKEN",
  // Define here a list of email addresses and telegram chats that will receive emails sent to these addresses.
  // Use an asterisk "*" instead of an email address to send all emails to some telegram chat.
  // "Prefixes" prepends an emoji or text to the message when the subject or body matches the regular expression.
  // "Actions" runs a command (for example a script) after a message is delivered.
  "Routing": [
    {
      "EmailTo": "*",
      "EmailFrom": "example@test.com",
      "TelegramChatId": 123456789,
      "Prefixes": [
        {
          "RegexpSubject": "\\ASuccess",
          "Prefix": "🟢 "
        }
      ],
      "Actions": [
        {
          "Name": "more",
          "Command": "c:\\Tools\\scripts\\run.ps1"
        }
      ]
    }
  ],
  // Logging Level. Set to Debug to see the details of the communication between your mail program and the relay.
  // Set to Error to see less information
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  },
  // NLog writes logs to the console and to a file in the logs folder.
  // To see more or fewer details change "minlevel" of the last rule (logger "*").
  "NLog": {
    "targets": {
      "File": {
        "type": "File",
        "fileName": "logs/${shortdate}.log",
        "layout": {
          "type": "CsvLayout",
          "quoting": "Auto",
          "withHeader": false,
          "delimiter": "Tab",
          "columns": [
            { "name": "DateTime", "layout": "${longdate}" },
            { "name": "Severity", "layout": "${uppercase:${level}}" },
            { "name": "Message", "layout": "${message}" },
            { "name": "Exception", "layout": "${exception:format=tostring}" }
          ]
        }
      },
      "Console": {
        "type": "ColoredConsole"
      }
    },
    "rules": [
      { "logger": "Microsoft.Hosting.Lifetime", "minlevel": "Info", "writeTo": "Console, File", "final": true },
      { "logger": "Microsoft.*", "maxlevel": "Debug", "final": true },
      { "logger": "System.Net.Http.*", "maxlevel": "Debug", "final": true },
      { "logger": "*", "minlevel": "Trace", "writeTo": "Console, File" }
    ]
  }
}
```
2. Register and run
2.1. Run `SmtpTelegramRelay.exe` as a standalone application
2.2. or register the program as a windows service `sc.exe create "SMTP Telegram Relay" binpath="C:\Program Files\SmtpTelegramRelay\SmtpTelegramRelay.exe" start=auto obj="NT AUTHORITY\LocalService"`
then start the windows service `sc.exe start "SMTP Telegram Relay"`
2.3. or register the program as a systemd service in unix-like operating systems. Create a configuration file `/etc/systemd/system/smtp-telegram-relay.service` looking as follows:
    ```ini
    [Unit]
    Description=SMTP Telegram Relay
    [Service]
    Type=simple
    ExecStart=/usr/sbin/SmtpTelegramRelay
    [Install]
    WantedBy=multi-user.target
    ```
    Then say systemd to load the new configuration file `sudo systemctl daemon-reload` and run the service `sudo systemctl start smtp-telegram-relay.service`

3. Send a test email and get it in telegram. Use `localhost` as an SMTP server address, `25` as a port and no authentifiacation or, if necessary, select the basic authentication method with a fake username and password.

# HTTP API

The relay also listens on `HttpAddress:HttpPort` and exposes a small HTTP API.

| Method | Route     | Description                                                                 |
| ------ | --------- | --------------------------------------------------------------------------- |
| GET    | `/health` | Health check. Returns `200` when the relay is healthy, `503` otherwise.     |
| GET    | `/message` | Deliver a message to Telegram. All fields are query parameters (see below). |
| POST   | `/message` | Deliver a message with file attachments to Telegram (multipart/form-data).  |

Query parameters of `/message` (both GET and POST):

- `from` – sender address (shown in the message header).
- `to` – recipient address, used for routing.
- `subject` – message subject.
- `message` – message body.
- `parseMode` – Telegram parse mode: `None`, `Html`, `Markdown` or `MarkdownV2`.
- `chatId` – override the Telegram chat ID configured in `Routing`.
- `hideFrom` – `true` to hide the sender address from the message.

Example:

```
curl "http://localhost:5026/message?to=*&subject=Hello&message=Hi%20there&parseMode=Markdown"
```

POST `/message` routes messages the same way and additionally sends the uploaded files to Telegram (photos are sent as photos, everything else as documents).
