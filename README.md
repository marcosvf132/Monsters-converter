# Monster-Converter
 DevM Monster Converter is a tool that can make server development easier.
 
 Convert your OpenTibia monsters loot from clientID/serverID/name to clientID/serverID.

 This monster converter was created to work along with Canary and OpenTibiaBR repository but can work on another servers.
 
  > On different server bases and versions may demand adaptation to work as intended.

# How to use
 - Compile the program with Visual studio or download the compiled release.
 - Open the executable file.
 - If you wan't to have more interaction with the process or track any erros, click on 'Log' button.
 - Click on 'Open' button to load your items.otb file.
 - Click on 'File' if you wan't to convert only one file or 'Folder' to convert all lua files on folder and subfolders
 - Chose the type of conversion. Select the input type of the files on the first box and the output type you desire to convert.
 - For the last, click on 'Convert' button and then it will pop a window for you to select your items.xml file.
 - Once you selected your items.xml file, the program will start converting your files to the desired type.
 
# Faq
  > Why it's ignoring some files?
 - Files are ignored because there is no need of conversion on then.
 
  > 'Item with name 'xxxxx' doesn't exist on OTB file.'
 - If your conversion type are similar (E.g Name/ClientID -> ClientID), the program will handle the error by itself. If the conversion type is not similar then it will ignore the conversion and won't fix the problem, you will have to update your items.otb to convert all files.
 

# Need help?
 - Feel free to message me on Discord. Check the 'about' label below.

# Compiling
 Open the project on Visual Studio and just hit Build. Packages used:
  - [MaterialDesignThemes.MahApps v0.1.9](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
  - [MahApps.Metro v2.4.9](https://github.com/MahApps/MahApps.Metro)
  - [Costura.Fody v5.7](https://github.com/Fody/Costura)
 
 
# About
 Tool created by Marcosvf132. You can message me on discord if you have any doubts or wan't to contribute somehow:
  > Discord: Marcosvf132#8947
