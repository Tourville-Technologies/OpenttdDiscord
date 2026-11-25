
# Openttd Discord

![GitHub](https://img.shields.io/github/license/shoter/OpenttdDiscord?style=plastic)
![Test badge](https://github.com/shoter/OpenttdDiscord/actions/workflows/test.yml/badge.svg)
[![codecov](https://codecov.io/gh/shoter/OpenttdDiscord/branch/master/graph/badge.svg?token=1EC4AKIMD3)](https://codecov.io/gh/shoter/OpenttdDiscord)

Provides ability to integrate Discord with your OpenTTD servers.

## Documentation

[Documentation link](https://shoter.github.io/OpenttdDiscord/)

## Functionalities

* Relays chat between the registered OpenTTD server and Discord.
    * Automatically translates ASCII emojis into Discord emojis and vice-versa.
* Ability to execute rcon commands on server from Discord.
* Ability to gather short reports from players about malicious behavior of other players
    * Contains information about connected clients
    * Contains last chat messages
    * Contains reason for the report
    * Some additional information
* Creating auto-updated messages which can show status of current server.
    * It contains the same information as OpenTTD client on server browser screen.
* Auto reply system
    * Players joining server are going to be welcomed with pre-defined message



## Discord game controls

Register the dedicated server with `/register-ottd-server` using its encrypted admin port. In the Discord channel where you want game messages, run `/register-chat-channel`. The relay posts public game chat, player joins and departures, and company changes. Company and private chat stay in-game. Enable **Message Content Intent** for Discord-to-game chat and give the bot **Send Messages**, **Embed Links**, and **Attach Files** in the relevant channels.

| Command | Result |
| --- | --- |
| `/game status` | Shows the registered server's map, game date, players, and live pause state. |
| `/game profit` | Uses the same quarterly operating profit as OpenTTD's Operating Profit graph. Shows all companies on one graph by default. Pass `company-id` to show one company. Open the SVG attachment in a browser to view it. |
| `/game link` | Gives you a private, one-use code to enter in OpenTTD's **All** chat. |
| `/game my-profit` | Graphs the companies your linked OpenTTD clients are currently playing in. Run `/game link` again when you reconnect. |
| `/game reset-password` | Clears the password for the company your linked OpenTTD client currently plays in. Specify `company` if your linked clients play in multiple companies. Set a new password in OpenTTD. |
| `/game unpause` | Opens a five-minute vote after at least one player joins the paused game. Use the same command with `vote:Yes` or `vote:No` to cast or change your vote. The vote passes if at least 50% of votes cast are yes when the poll closes. The bot posts the result in the channel where the vote opened. |
| `/game-admin start`, `stop`, `restart` | Runs the configured service command on the bot host. |
| `/game-admin ping-role` | Saves the Discord role to ping once when a vote opens for this server. Omit `role` to clear it. |
| `/game-admin clear-password` | Removes an existing human company's password. The company stays in the game, and its players can set a new password in OpenTTD. |

Commands use the single OpenTTD server registered for the Discord server. Register it once with `/register-ottd-server`. The `/game-admin` group requires Discord **Administrator** permission. The configured role must be mentionable or the bot must have permission to mention all roles in the vote channel.

### Dedicated server patch

[The companion patch](openttd/discord-dedicated.patch) targets the `more-companies-1.0.5` release of [Tourville Technologies' OpenTTD fork](https://github.com/Tourville-Technologies/OpenTTD-patches/tree/more-companies-1.0.5). Apply it from that OpenTTD checkout, then build and run your dedicated server as usual.

```sh
git apply /path/to/OpenttdDiscord/openttd/discord-dedicated.patch
cmake -S . -B build-discord -DOPTION_DEDICATED=ON
cmake --build build-discord --parallel
```

Set `min_active_clients = 0` and `pause_on_join = false` in the server's `[network]` configuration so the vote controls resuming after the server empties. The patched server disconnects players and spectators after 15 minutes without an in-game command, chat message, or company move. Camera movement and network keepalives do not count as activity. The game pauses when the last player leaves, including after an AFK disconnect, and players can join while it is paused. Basic status and public chat also work with servers that do not have the patch.

### Start, stop, and restart configuration

Set the bot's `OpenTTD:Services` configuration to match the Discord guild ID and registered server name. Each action is an argument array beginning with an absolute executable path. This example uses a Linux systemd unit that the bot's service account has permission to manage.

```json
{
  "OpenTTD": {
    "Services": [
      {
        "GuildId": "123456789012345678",
        "Server": "main",
        "start": ["/usr/bin/systemctl", "start", "openttd.service"],
        "stop": ["/usr/bin/systemctl", "stop", "openttd.service"],
        "restart": ["/usr/bin/systemctl", "restart", "openttd.service"]
      }
    ]
  }
}
```

With environment variables, use keys such as `OpenTTD__Services__0__GuildId` and `OpenTTD__Services__0__restart__0`. For a remote server or a containerized bot, configure an executable visible to the bot that manages the remote service. Configure your service to save and reload the desired world across restarts.

## Compilation/Installation

For Docker Compose, export `discord_token` and run `bash run.sh` from the repository root. Compose starts the bot and PostgreSQL. The bot container applies database migrations on startup.

For a direct .NET 10 run, set `Discord__Token` and `Database__ConnectionString` for your PostgreSQL database. Apply the repository's migrations, then run `dotnet run --project OpenttdDiscord.Discord`. To load service commands from a JSON file, put the `OpenTTD` section in the application's `appsettings.json`.

## Bots required permissions

- View channels
- Send Messages
- Embed files

Read more about permissions in [official Discord documentation](https://discord.com/developers/docs/topics/permissions)

## Development

### Conventional commits

This repository uses [Conventional commits](https://www.conventionalcommits.org/en/v1.0.0/) to specify type of commits commited to the repository.

Used types:
- feat - feature
- fix
- ref - refactor
- docs - documentation
- nuget - commits connected with nuget upgrade/downgrade
- chore


## Architecture

### Actors hierarchy

![Actors hierarchy](https://raw.githubusercontent.com/shoter/OpenttdDiscord/master/obsidian/docs/diagrams/akka.drawio.png)

### Database diagram

![Database diagram](https://raw.githubusercontent.com/shoter/OpenttdDiscord/master/obsidian/docs/diagrams/database.drawio.png)

## Test coverage

[![Test coverage graph](https://codecov.io/gh/shoter/OpenttdDiscord/branch/master/graphs/tree.svg?token=1EC4AKIMD3)](https://app.codecov.io/gh/shoter/OpenttdDiscord)
