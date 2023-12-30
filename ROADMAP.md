# Radar Ident QuickSwitch Roadmap

## Items in the Roadmap

In no particular order...

### Config Option to Start at 1 (default 0)

- Add a configuration option to start the Radar Ident numbering at 1 instead of the default 0, letters X instead of A, etc.
- Allow users to customize the starting point according to their preference.

### Config Option to backfill numbers (default: true)

- Introduce a configuration option to enable or disable back-filling numbers for the Radar Ident.
- By default, enable back-filling to ensure sequential numbering and less typing.

### Reserved Identification/Callsigns

- Implement a mechanism to reserve specific identification or callsigns for special purposes, players or groups.

### Alpha Callsigns using the NATO phonetic alphabet

- Add support for alpha callsigns using the NATO phonetic alphabet.

### Various Terminal commands

- Implement a set of terminal commands to manage and configure the Radar Ident QuickSwitch mod.
- Provide commands for customization, configuration, and troubleshooting.

## Other Mods in Development

### Draw Callsigns on Player Suit

- Implement a feature to draw the Radar Ident on the player's suit.
- Ensure the Radar Ident is visible and easily identifiable.

## Terminal Commands

- `riqs.help`

  Displays this list of available commands and their usage.

- `riqs.assign <callsign> <player/group>`

  Assigns the specified callsign to the given player or group.

- `riqs.reserve <callsign> <player/group>`

  Reserves the specified callsign for the given player or group.

- `riqs.reset`

  Forcibly re-assigns all callsigns to new values.

- `riqs.<key> <value>`

  Configures a specific option of the Radar Ident QuickSwitch mod.

  See Config Options below for a list of available options.

  E.g.: `riqs.type nato`

## Config Options

- `enabled`

  Whether or not the Radar Ident QuickSwitch mod is enabled. (default: true)

- `backfill`

  Whether or not to backfill and re-use leavers' identifiers. (default: true)

- `length`

  The length of the Number or Latin identifier to use. (default: 1, options: 1-8)

- `sequential`

  Whether or not to use sequential identifiers (random otherwise). (default: true)

- `start`

  The start point for the Number or Latin identifier. (default: 0 or A, options: 0-9, A-Z)

- `type`

  The type of identifier to use. (default: number, options: number, latin, greek, nato)
