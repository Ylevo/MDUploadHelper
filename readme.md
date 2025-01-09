# MangaDex Upload Helper

A CLI toolbox to perform various operations in a somewhat automated way before you upload your chapters on MangaDex.


## Usage

Download the [latest version](https://github.com/Ylevo/TMOScrapHelper/releases/latest) from the releases page, unzip it, edit the `settings.json` to fit your needs (see [settings](#settings)) then run the exe. You'll be asked to enter the path to the main folder, containing your chapters.

- It uses the same naming convention as [mupl](https://github.com/ArdaxHz/mupl/tree/main) and is intended to be used with it. Any folder who doesn't follow this format will be ignored.

- Your chapters **MUST** be sorted by titles using subfolders.

#### Example
You have chapters for three different titles: `Kanokari`, `Henjo` and `67% Inertia`. You create three folders with these names and put your chapters, named like `Kanokari [es-la] - c753 (v153) [I love Kanokari]`, into their respective folder. You now have three title folders in the same folder: `D:\Weeb\Kanokari`, `D:\Weeb\Henjo` and `D:\Weeb\67% Inertia`. When you run the program and it asks for the main folder, you enter `D:\Weeb`.

## Settings

Settings can be changed in the `settings.json` file.

- `uploaderFolder` Path to your mupl folder.
- `languages` Array of ISO language codes. Used in most operations that involve fetching something from MD. Set it according to the language(s) of your chapters.
- `skipMainFolder` Flag to skip the initial main folder's path input. If you're only interested in the *miscellaneous* commands, set it to true.
- `aggregateGistId` Ignore it if you're not scraping TMO.

## List of commands
- [General](#general)
  - [Get groups' names](#Get-groups'-names)
  - [Match titles](#Match-titles)
  - [Compare mango titles](#Compare-mango-titles)
  - [Check for duplicates](#Check-for-duplicates)
  - [Find volume numbers](#Find-volume-numbers)
  - [Check for already uploaded chapters](#Check-for-already-uploaded-chapters)
  - [Log chapters](#Log-chapters)
- [Bulk Uploader](#Bulk-Uploader)
  - [Merge json maps](#Merge-json-maps)
  - [Move chapters to uploader](#Move-chapters-to-uploader)
  - [Move chapters back from uploader](#Move-chapters-back-from-uploader)
- [Miscellaneous](#Miscellaneous)
  - [Fetch chapter ids using creation time](#Fetch-chapter-ids-using-creation-time)
  - [List titles with uploads from a user](#List-titles-with-uploads-from-a-user)
  - [Check chapter count of titles](#Check-chapter-count-of-titles)
- [TMO Related](#TMO-Related)
  - [Merge local json with online aggregate](#Merge-local-json-with-online-aggregate)
  - [Check for duplicates in aggregate](#Check-for-duplicates-in-aggregate)

## General
### Get groups' names
Fetches the groups' names from your chapter folders and serialize them into a `name_id_map.json` file in the main folder, identical in format to mupl's. If `name_id_map.json` already exists, it only adds missing entries.

If `uploaderFolder` has been set, it looks for existing values in mupl's `name_id_map.json`.

This does **not** automatically match & fetch the ids from MD. You have to do that manually.

#### Settings used: 
- `uploaderFolder` *(optional)*

### Match titles
Searches on MD for title entries corresponding to the titles you have chapters for and serialize the name/id pairs into a `name_id_map.json` file in the main folder. If `name_id_map.json` already exists, the entries are **overwritten**.

If `uploaderFolder` has been set, it looks for existing values in mupl's `name_id_map.json`.

For each title, the first five (at most) search results are returned. You can then either pick one, skip by pressing enter, or enter the id directly. If skipped or no result is returned, `None picked` and `Not found` will respectively be set as value.

Alt titles are presented for each result according to your `languages` setting.

A log file `titleMatchingLog.txt` is created in the main folder.

#### Settings used: 
- `uploaderFolder` *(optional)*
- `languages` *(optional)*

### Compare mango titles 

This is essentially a double checking operation after matching titles. It displays both the main title and the alt titles according to your `languages` setting for every title name with a valid value in`name_id_map.json`.

#### Settings used:
- `languages` *(optional)*

### Check for duplicates 

Another double (triple?) checking operation. It looks for ids assigned to more than one key in the main folder's `name_id_map.json`, i.e. if you've matched more than one title name to an MD title entry. It also looks into mupl's map for the same thing. This operation is always executed at the end of [title matching](#Match-titles).

#### Settings used:
- `uploaderFolder` *(optional)*

### Find volume numbers

For each title, retrieves existing volume numbers from their matched MD title entry and add them to your chapter folders. Does nothing to chapter folders with a volume number. It handles both folders with `(v)` and nothing at all.

If the title entry's chapter numbers reset with every volume released or there are discrepancies among the volume numbers, it warns you about it and asks for confirmation to proceed.

Oneshots are skipped.

### Check for already uploaded chapters

As the name says, it checks on MD for existing chapters so you don't upload duplicates. You need both groups and titles matched with their corresponding MD ids in your `name_id_map.json` for this. 

**Because some languages *overlap* (such as es/es-la), this checks chapters based on your `languages` setting, and not on the language of the chapter folder.**

#### Settings used:
- `languages`

### Log chapters

Creates a `chaptersLog_<timestamp>.txt` log file, a snapshot of all your chapter folders, in the main folder. Strongly suggest making one before uploading anything to keep a trace of them and making one before/after doing anything altering them.

## Bulk Uploader

### Merge json maps

Merges the main folder's `name_id_map.json` with mupl's. Main folder's map takes priority and will overwrite existing entries' values. A copy of mupl's map is created as backup beforehand.

#### Settings used:
- `uploaderFolder`

### Move chapters to uploader

Self-explanatory. Only the chapter folders are moved, not the titles subfolders.

#### Settings used:
- `uploaderFolder`

### Move chapters back from uploader

Same as above but backwards. When you realized you screwed up or forgot something.

#### Settings used:
- `uploaderFolder`

## Miscellaneous

### Fetch chapter ids using creation time

Fetches chapter ids created/uploaded between two dates, including. When you've screwed up and can't distinguish chapters anymore merely by uploader and group.

### List titles with uploads from a user

Fetches **ALL** chapters uploaded by a user and dump the title ids to which they belong in a text file. Be careful who you try that on to.

### Check chapter count of titles

Another self-explanatory command. This returns the chapter count of the title urls/ids entered for the languages set in settings. One url/id per line, copy pasta friendly. Enter an empty line to confirm.

#### Settings used:
- `languages`

## TMO Related

### Merge local json with online aggregate

Same thing than [merge json maps](#Merge-json-maps) except with the online aggregate for TMO scrapers. **The online aggregate takes priority over existing values.** 

You can now use whatever gist you want with the setting `aggregateGistId`, in case I disappear or if you hate my guts.

#### Settings used:
- `uploaderFolder`
- `aggregateGistId`

### Check for duplicates in aggregate

Looks through the online aggregate for ids assigned to more than one key, to know if anyone fucked up. Many such cases.

#### Settings used:
- `uploaderFolder`
- `aggregateGistId`

--------------------------
## Contribution

Open an issue or ask me on discord for any command you would like to have added.

## Credits

- [MangaDexSharp](https://github.com/calico-crusade/mangadex-sharp) - a C# wrapper for MD API that makes my life easier.
- [Pastel](https://github.com/silkfire/Pastel) - to give the console a bit of colour.
- [ConsoleProgressBar](https://github.com/iluvadev/ConsoleProgressBar)


