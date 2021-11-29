using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Xml;

namespace Canary_monster_converter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        #region Enums and utils
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        enum SpecialChar : byte
        {
            IndexKey = 0x6D,        // 'm'
            CommentKey = 0x2D,        // '-'
            JumpKey = 0x0A,         // Jumpline
            StringHeader = 0x22,    // '"'
            ColchetStart = 0x7B,    // '{'
            ColchetEnd = 0x7D,      // '}'
            CommaKey = 0x2C,        // ','
            NodeStart = 0xFE,       // 254 (OTB Only)
            EscapeChar = 0xFD,      // 253 (OTB Only)
            NodeEnd = 0xFF          // 255 (OTB Only)
        }

        public enum RootAttribute : byte
        {
            Version = 0x01
        }

        public enum ItemGroup : byte
        {
            None = 0x00,
            Ground = 0x01,
            Container = 0x02,
            Weapon = 0x03,
            Ammunition = 0x04,
            Armor = 0x05,
            Changes = 0x06,
            Teleport = 0x07,
            MagicField = 0x08,
            Writable = 0x09,
            Key = 0x0A,
            Splash = 0x0B,
            Fluid = 0x0C,
            Door = 0x0D,
            Deprecated = 0x0E
        }

        public enum ItemFlag
        {
            None = 0,
            Unpassable = 1 << 0,
            BlockMissiles = 1 << 1,
            BlockPathfinder = 1 << 2,
            HasElevation = 1 << 3,
            MultiUse = 1 << 4,
            Pickupable = 1 << 5,
            Movable = 1 << 6,
            Stackable = 1 << 7,
            FloorChangeDown = 1 << 8,
            FloorChangeNorth = 1 << 9,
            FloorChangeEast = 1 << 10,
            FloorChangeSouth = 1 << 11,
            FloorChangeWest = 1 << 12,
            StackOrder = 1 << 13,
            Readable = 1 << 14,
            Rotatable = 1 << 15,
            Hangable = 1 << 16,
            HookSouth = 1 << 17,
            HookEast = 1 << 18,
            CanNotDecay = 1 << 19,
            AllowDistanceRead = 1 << 20,
            Unused = 1 << 21,
            ClientCharges = 1 << 22,
            IgnoreLook = 1 << 23,
            IsAnimation = 1 << 24,
            FullGround = 1 << 25,
            ForceUse = 1 << 26
        }

        public enum ItemAttribute : byte
        {
            ServerID = 0x10,
            ClientID = 0x11,
            Name = 0x12,
            GroundSpeed = 0x14,
            SpriteHash = 0x20,
            MinimaColor = 0x21,
            MaxReadWriteChars = 0x22,
            MaxReadChars = 0x23,
            Light = 0x2A,
            StackOrder = 0x2B,
            TradeAs = 0x2D
        }

        public enum ReturnValue
        {
            Error = 0,
            Success = 1,
            Ignored = 2
        }

        public enum Convert_t
        {
            NameAndServerID = 0,
            NameAndClientID = 1,
            ClientID = 2,
            ServerID = 3,
            Name = 4
        };

        // File header used to identify it was already converted.
        // This has no effect on the converter, only for the user.
        private string _fileHeader = "--# Monster converted using Devm monster converter #--";

        // The following headers are default for all OTServerBR scripts (so far) and for canary too.
        // In case your system use another name declarations you need to change the following parts and on SpecialChar.
        private string _CorpseHeader = "monster.corpse = ";

        private string _LootHeader = "monster.loot = {";

        private string _LootIDChildHeader = "{id = ";

        private string _LootNameChildHeader = "{name = ";

        private string _itemsNode = "items";

        private string _itemNode = "item";

        private string _xmlPath = string.Empty;

        private static BackgroundWorker _worker;

        private bool _isFolder;

        private string _path;

        private List<Item> _items;

        private int _progress;

        private Convert_t _ConvertFromType;

        private Convert_t _ConvertToType;

        public class Item
        {
            public Item(ushort serverId, ushort clientId)
            {
                ServerId = serverId;
                ClientId = clientId;
                Name = string.Empty;
                Provisory = false;
            }
            public string Name { get; set; }

            public ushort ServerId { get; set; }
            public ushort ClientId { get; set; }

            public bool SubType { get; set; }
            public bool Provisory { get; set; }
        }
        
        public class BinaryTreeReader : IDisposable
        {
            #region Private Properties

            private BinaryReader reader;
            private long currentNodePosition;
            private uint currentNodeSize;

            #endregion

            #region Constructor

            public BinaryTreeReader(string path)
            {
                if (string.IsNullOrEmpty(path)) {
                    throw new ArgumentNullException("input");
                }

                this.reader = new BinaryReader(new FileStream(path, FileMode.Open));
                this.Disposed = false;
            }

            #endregion

            #region Public Properties

            public bool Disposed { get; private set; }

            #endregion

            #region Public Properties

            public BinaryReader GetRootNode()
            {
                return GetChildNode();
            }

            public BinaryReader GetChildNode()
            {
                Advance();
                return GetNodeData();
            }

            public BinaryReader GetNextNode()
            {
                reader.BaseStream.Seek(currentNodePosition, SeekOrigin.Begin);

                SpecialChar value = (SpecialChar)reader.ReadByte();
                if (value != SpecialChar.NodeStart)
                    return null;

                value = (SpecialChar)reader.ReadByte();

                int level = 1;
                while (true) {
                    value = (SpecialChar)reader.ReadByte();
                    if (value == SpecialChar.NodeEnd) {
                        --level;
                        if (level == 0) {
                            value = (SpecialChar)reader.ReadByte();
                            if (value == SpecialChar.NodeEnd) {
                                return null;
                            } else if (value != SpecialChar.NodeStart) {
                                return null;
                            } else {
                                currentNodePosition = reader.BaseStream.Position - 1;
                                return GetNodeData();
                            }
                        }
                    } else if (value == SpecialChar.NodeStart) {
                        ++level;
                    } else if (value == SpecialChar.EscapeChar) {
                        reader.ReadByte();
                    }
                }
            }

            public void Dispose()
            {
                if (reader != null) {
                    reader.Dispose();
                    reader = null;
                    Disposed = true;
                }
            }

            #endregion

            #region Private Methods

            private BinaryReader GetNodeData()
            {
                reader.BaseStream.Seek(currentNodePosition, SeekOrigin.Begin);

                // read node type
                byte value = reader.ReadByte();

                if ((SpecialChar)value != SpecialChar.NodeStart)
                    return null;

                MemoryStream ms = new MemoryStream();

                currentNodeSize = 0;
                while (true) {
                    value = reader.ReadByte();
                    if ((SpecialChar)value == SpecialChar.NodeEnd || (SpecialChar)value == SpecialChar.NodeStart)
                        break;
                    else if ((SpecialChar)value == SpecialChar.EscapeChar)
                        value = reader.ReadByte();

                    currentNodeSize++;
                    ms.WriteByte(value);
                }

                reader.BaseStream.Seek(currentNodePosition, SeekOrigin.Begin);
                ms.Position = 0;
                return new BinaryReader(ms);
            }

            private bool Advance()
            {
                try
                {
                    long seekPos = 0;
                    if (currentNodePosition == 0)
                        seekPos = 4;
                    else
                        seekPos = currentNodePosition;

                    reader.BaseStream.Seek(seekPos, SeekOrigin.Begin);

                    SpecialChar value = (SpecialChar)reader.ReadByte();
                    if (value != SpecialChar.NodeStart)
                        return false;

                    if (currentNodePosition == 0) {
                        currentNodePosition = reader.BaseStream.Position - 1;
                        return true;
                    } else {
                        value = (SpecialChar)reader.ReadByte();

                        while (true) {
                            value = (SpecialChar)reader.ReadByte();
                            if (value == SpecialChar.NodeEnd) {
                                return false;
                            } else if (value == SpecialChar.NodeStart) {
                                currentNodePosition = reader.BaseStream.Position - 1;
                                return true;
                            } else if (value == SpecialChar.EscapeChar) {
                                reader.ReadByte();
                            }
                        }
                    }
                } catch (Exception) {
                    return false;
                }
            }

            #endregion
        }
        
        public void ReadLuaFolderSubFolder(string path)
        {
            try
            {
                int errors = 0;
                int success = 0;
                int ignored = 0;
                foreach (string f in Directory.GetFiles(path, "*.lua")) {
                    string extension = Path.GetExtension(f);
                    if (extension != null && extension.Equals(".lua")) {

                        // Read the file
                        ReturnValue lua = ParseLuaAsBinary(f);
                        if (lua == ReturnValue.Success) {
                            success++;
                        } else if (lua == ReturnValue.Ignored) {
                            ignored++;
                        } else {
                            errors++;
                        }
                    }
                }
                Console.WriteLine("[INFO] Folder: " + path +
                    " \n  Converted: " + success +
                    " \n  Ignored: " + ignored +
                    " \n  Errors: " + errors);

                foreach (string d in Directory.GetDirectories(path)) {
                    ReadLuaFolderSubFolder(d);
                }

            } catch (Exception ex) {
                Console.WriteLine("[ERROR::FOLDERS] " + ex.Message);
            }
        }
        
        private Item GetItemByID(ushort itemid, bool isClientId)
        {
            foreach (Item item in _items) {
                if (isClientId && item.ClientId == itemid ||
                    !isClientId && item.ServerId == itemid) {
                    return item;
                }
            }

            return null;
        }

        private ushort GetConvertedItemID(ushort serverid)
        {
            // Some monsters doesn't have registered corpse id.
            if (serverid == 0 ||
                (_ConvertFromType == Convert_t.NameAndClientID && _ConvertToType == Convert_t.ClientID) ||
                (_ConvertFromType == Convert_t.NameAndServerID && _ConvertToType == Convert_t.ServerID)) {
                return serverid;
            }

            foreach (Item item in _items) {
                if ((_ConvertFromType == Convert_t.NameAndServerID || _ConvertFromType == Convert_t.ServerID) && _ConvertToType == Convert_t.ClientID) {
                    if (item.ServerId == serverid) {
                        return item.ClientId;
                    }
                } else if ((_ConvertFromType == Convert_t.NameAndClientID || _ConvertFromType == Convert_t.ClientID) && _ConvertToType == Convert_t.ServerID) {
                    if (item.ClientId == serverid) {
                        return item.ServerId;
                    }
                }
            }

            throw new Exception("Item with id " + serverid + " not found.");
        }        

        private ushort GetConvertedItemName(string name)
        {
            // To prevent some incorrect monster loot.
            if (name.Length == 0) {
                return 0;
            }

            foreach (Item item in _items) {
                if (item.Name.ToLower() == name.ToLower()) {
                    if (_ConvertToType == Convert_t.ClientID) {
                        return item.ClientId;
                    } else if (_ConvertToType == Convert_t.ServerID) {
                        return item.ServerId;
                    }
                }
            }

            throw new Exception("Item with name '" + name + "' not found.");
        }

        private void ClearProvisoryItems()
        {
            foreach (Item item in _items) {
                if (item.Provisory) {
                    _items.Remove(item);
                }
            }
        }
        #endregion

        #region Binary methods
        private bool IsConverterHeaderString(BinaryReader reader)
        {
            byte size = (byte)_fileHeader.Length;

            var pos = reader.BaseStream.Position;
            if ((pos + size) >= reader.BaseStream.Length) {
                return false;
            }

            reader.BaseStream.Position = pos - 1;

            byte[] buffer = new byte[size];
            int bytesRead = reader.Read(buffer, 0, buffer.Length);
            if (bytesRead != size)
                return false;

            string text = Encoding.UTF8.GetString(buffer);
            bool rt = text == _fileHeader;

            // Jumpline byte
            if (rt) {
                reader.ReadByte();
            } else {
                reader.BaseStream.Position = pos;
            }

            return rt;
        }

        private bool IsCorpseString(BinaryReader reader, MemoryStream output)
        {
            byte size = (byte)_CorpseHeader.Length;

            var pos = reader.BaseStream.Position;
            if ((pos + size) >= reader.BaseStream.Length) {
                return false;
            }

            reader.BaseStream.Position = pos - 1;

            byte[] buffer = new byte[size];
            int bytesRead = reader.Read(buffer, 0, buffer.Length);
            if (bytesRead != size)
                return false;

            string text = Encoding.UTF8.GetString(buffer);
            bool rt = text == _CorpseHeader;

            if (!rt) {
                reader.BaseStream.Position = pos;
            } else {
                output.Write(buffer);
            }

            return rt;
        }

        private bool IsLootString(BinaryReader reader, MemoryStream output)
        {
            byte size = (byte)_LootHeader.Length;

            var pos = reader.BaseStream.Position;
            if ((pos + size) >= reader.BaseStream.Length) {
                return false;
            }

            reader.BaseStream.Position = pos - 1;

            byte[] buffer = new byte[size];
            int bytesRead = reader.Read(buffer, 0, buffer.Length);
            if (bytesRead != size)
                return false;

            string text = Encoding.UTF8.GetString(buffer);
            bool rt = text == _LootHeader;
            
            if (!rt) {
                reader.BaseStream.Position = pos;
            } else {
                output.Write(buffer);
            }

            return rt;
        }

        private bool IsLootId(BinaryReader reader, MemoryStream output)
        {
            byte size = (byte)_LootIDChildHeader.Length;

            var pos = reader.BaseStream.Position;
            reader.BaseStream.Position = pos - 1;

            byte[] buffer = new byte[size];
            int bytesRead = reader.Read(buffer, 0, buffer.Length);
            if (bytesRead != size)
                return false;

            string text = Encoding.UTF8.GetString(buffer);
            bool rt = text == _LootIDChildHeader;
            
            if (!rt) {
                reader.BaseStream.Position = pos;
            } else {
                output.Write(buffer);
            }

            return rt;
        }

        private bool IsLootName(BinaryReader reader)
        {
            byte size = (byte)_LootNameChildHeader.Length;

            var pos = reader.BaseStream.Position;
            reader.BaseStream.Position = pos - 1;

            byte[] buffer = new byte[size];
            int bytesRead = reader.Read(buffer, 0, buffer.Length);
            if (bytesRead != size)
                return false;

            string text = Encoding.UTF8.GetString(buffer);
            bool rt = text == _LootNameChildHeader;
            
            if (!rt) {
                reader.BaseStream.Position = pos;
            }

            return rt;
        }
        
        private ushort GetServerId(BinaryReader reader, byte size)
        {
            byte[] buffer = new byte[size];
            int bytesRead = reader.Read(buffer, 0, buffer.Length);
            string corpseId = Encoding.UTF8.GetString(buffer);
            return ushort.Parse(corpseId);
        }

        private string GetItemName(BinaryReader reader, byte size)
        {
            if (reader.ReadByte() != (byte)SpecialChar.StringHeader) {
                return "";
            }

            byte[] buffer = new byte[size - 2];
            int bytesRead = reader.Read(buffer, 0, buffer.Length);
            
            if (reader.ReadByte() != (byte)SpecialChar.StringHeader) {
                return "";
            }

            return Encoding.UTF8.GetString(buffer);
        }

        #endregion

        #region Binary reader
        private ReturnValue ParseLuaAsBinary(string path)
        {
            ReturnValue rt = ReturnValue.Success;
            try
            {
                // Files that doesn't use id on loot and no corpse regisreted then we can ignore the writing on it.
                bool ignore = true;
                
                // Used to identify if the binary writer should write comments on the end of the loot line.
                string insertComment = string.Empty;

                //  Memory stream is the output stream.
                //  File stream is the input stream.
                //  Binary reader is the input reader.
                //  The output is writing using a simple MemoryStream.WriteTo() method.
                using (MemoryStream output = new MemoryStream()) {

                    // Writing converter header. Used just so the user can identify the changes.
                    output.Write(Encoding.UTF8.GetBytes(_fileHeader));
                    output.WriteByte((byte)SpecialChar.JumpKey);

                    using (FileStream stream = new FileStream(path, FileMode.Open)) {
                        using (BinaryReader reader = new BinaryReader(stream)) {
                            while (reader.PeekChar() != -1) {
                                var value = reader.ReadByte();

                                // We have found the index key byte. It represents the character m as lower.
                                if (value == (byte)SpecialChar.IndexKey) {

                                    // Reading the following bytes to seek for the corpse header.
                                    if (IsCorpseString(reader, output)) {
                                        byte bufferPos = 0;
                                        while (true) {
                                            var _byte = reader.ReadByte();

                                            // We have reached the end of the item id.
                                            if (_byte == (byte)SpecialChar.JumpKey) {
                                                break;
                                            }

                                            // Advancing the iterator indicator.
                                            bufferPos++;
                                        }

                                        // Reseting position to the beginning of the string.
                                        reader.BaseStream.Position = reader.BaseStream.Position - bufferPos - 1;

                                        // Reading the array of bytes as string and parse it to ushort. (uint16_t)
                                        ushort corpseId = GetServerId(reader, bufferPos);

                                        // Writing the converted corpse ID.
                                        output.Write(Encoding.UTF8.GetBytes(GetConvertedItemID(corpseId).ToString()));

                                        if (corpseId != 0) {
                                            ignore = false;
                                        }

                                    // Reading the following bytes to seek for the loot header.
                                    } else if (IsLootString(reader, output)) {
                                        int jumps = 0;
                                        while (true) {
                                            var _byte = reader.ReadByte();
                                            if (_byte == (byte)SpecialChar.ColchetStart) {
                                                jumps++;

                                                // Reading the following bytes to seek for the loot ID header.
                                                if (IsLootId(reader, output)) {
                                                    byte bufferPos = 0;
                                                    while (true) {
                                                        _byte = reader.ReadByte();

                                                        // We have reached the end of the item id.
                                                        if (_byte == (byte)SpecialChar.CommaKey || _byte == (byte)SpecialChar.ColchetEnd) {
                                                            break;
                                                        }

                                                        // Advancing the iterator indicator.
                                                        bufferPos++;
                                                    }

                                                    // Reseting position to the beginning of the string.
                                                    reader.BaseStream.Position = reader.BaseStream.Position - bufferPos - 1;

                                                    // Reading the array of bytes as string and parse it to ushort. (uint16_t)
                                                    ushort lootId = GetServerId(reader, bufferPos);

                                                    Item item = GetItemByID(lootId, _ConvertFromType == Convert_t.NameAndClientID || _ConvertFromType == Convert_t.ClientID);
                                                    if (item != null) {
                                                        insertComment = item.Name;
                                                    }

                                                    // Writing the converted loot ID.
                                                    output.Write(Encoding.UTF8.GetBytes(GetConvertedItemID(lootId).ToString()));

                                                    if (lootId != 0) {
                                                        ignore = false;

                                                    }

                                                // Reading the following bytes to seek for the loot name header.
                                                } else if (IsLootName(reader)) {
                                                    byte bufferPos = 0;
                                                    while (true) {
                                                        _byte = reader.ReadByte();

                                                        // We have reached the end of the item name.
                                                        if (_byte == (byte)SpecialChar.CommaKey || _byte == (byte)SpecialChar.ColchetEnd) {
                                                            break;
                                                        }

                                                        // Advancing the iterator indicator.
                                                        bufferPos++;
                                                    }

                                                    // Reseting position to the beginning of the string.
                                                    reader.BaseStream.Position = reader.BaseStream.Position - bufferPos - 1;

                                                    // Reading the array of bytes as string.
                                                    string lootName = GetItemName(reader, bufferPos);

                                                    // Writing the converted loot to ID.
                                                    if (_ConvertFromType == Convert_t.NameAndServerID || _ConvertFromType == Convert_t.NameAndClientID || _ConvertFromType == Convert_t.Name) {

                                                        // Writing loot header as ID now.
                                                        output.Write(Encoding.UTF8.GetBytes(_LootIDChildHeader));

                                                        // Writing the converter item name to ushort. (uint16_t)
                                                        output.Write(Encoding.UTF8.GetBytes(GetConvertedItemName(lootName).ToString()));
                                                        insertComment = lootName;
                                                        ignore = false;
                                                    } else {

                                                        // Writing loot header as ID now.
                                                        output.Write(Encoding.UTF8.GetBytes(_LootNameChildHeader));

                                                        output.WriteByte((byte)SpecialChar.StringHeader);
                                                        output.Write(Encoding.UTF8.GetBytes(lootName));
                                                        output.WriteByte((byte)SpecialChar.StringHeader);
                                                    }
                                                } else {
                                                    // The parser failed to find the header, so the missing key byte need writing.
                                                    // It's just a simple random byte, so we need to write it down on output.
                                                    output.WriteByte(_byte);
                                                }
                                            } else if (_byte == (byte)SpecialChar.ColchetEnd) {
                                                output.WriteByte(_byte);
                                                if (jumps == 0) {
                                                    break;
                                                }

                                                --jumps;
                                            } else if (jumps == 0 && insertComment.Length != 0 && _byte == (byte)SpecialChar.JumpKey) {
                                                // The last loot was converted from name to ushort, so we need to write a comment on that line so we can know which item are we talking about.
                                                output.Write(Encoding.UTF8.GetBytes(" -- " + insertComment));

                                                // Writing the jumpkey
                                                output.WriteByte(_byte);

                                                // Reseting comments
                                                insertComment = string.Empty;
                                            } else {
                                                // It's just a simple random byte, so we need to write it down on output.
                                                output.WriteByte(_byte);
                                            }
                                        }
                                    } else {
                                        // The parser failed to find the header, so the missing key byte need writing.
                                        // It's just a simple random byte, so we need to write it down on output.
                                        output.WriteByte(value);
                                    }
                                } else if (value == (byte)SpecialChar.CommentKey) {
                                    if (!IsConverterHeaderString(reader)) {
                                        output.WriteByte(value);
                                    }
                                } else {
                                    // The parser failed to find the header, so the missing key byte need writing.
                                    // It's just a simple random byte, so we need to write it down on output.
                                    output.WriteByte(value);
                                }
                            }

                            if (!ignore) {
                                // Since we reached the end of the original file, we need to erase it and write the new bytes.
                                stream.Position = 0;
                                stream.SetLength(0);

                                // Writing the new bytes already converted.
                                output.WriteTo(stream);
                            }
                        }
                    }
                }

                if (ignore) {
                    Console.WriteLine("[INFO] File ignored: " + path);
                    rt = ReturnValue.Ignored;
                } else {
                    Console.WriteLine("[INFO] File converted: " + path);

                    _progress++;
                    _worker.ReportProgress(_progress);
                }
            } catch (Exception ex) {
                Console.WriteLine("[ERROR::LUA] \n    Message: " + ex.Message +
                    "\n    File: " + path +
                    "\n    Tracer: " + ex.StackTrace);
                rt = ReturnValue.Error;
            }

            return rt;
        }

        private void ParseItemOtb(string path)
        {
            _items = new List<Item>();
            try {
                using (BinaryTreeReader reader = new BinaryTreeReader(path))
                {
                    // get root node
                    BinaryReader node = reader.GetRootNode();
                    if (node == null) {
                        throw new Exception("Error while parsing .otb file. #001");
                    }

                    // first byte of otb is 0
                    node.ReadByte();

                    // 4 bytes flags, unused
                    node.ReadUInt32();

                    byte attr = node.ReadByte();
                    if ((RootAttribute)attr == RootAttribute.Version) {
                        ushort datalen = node.ReadUInt16();
                        if (datalen != 140) { // 4 + 4 + 4 + 1 * 128
                            throw new Exception("Error while parsing .otb file. #002");
                        }

                        // major, file version
                        node.ReadUInt32();

                        // minor, client version
                        node.ReadUInt32();

                        // build number, revision
                        node.ReadUInt32();

                        node.BaseStream.Seek(128, SeekOrigin.Current);
                    }

                    node = reader.GetChildNode();
                    if (node == null) {
                        throw new Exception("Error while parsing .otb file. #003");
                    }

                    do {
                        // Group
                        ItemGroup group = (ItemGroup)node.ReadByte();

                        // Flags
                        ItemFlag flags = (ItemFlag)node.ReadUInt32();

                        ushort serverID = 0;
                        ushort clientID = 0;
                        while (node.PeekChar() != -1) {
                            ItemAttribute attribute = (ItemAttribute)node.ReadByte();
                            ushort datalen = node.ReadUInt16();

                            switch (attribute) {
                                case ItemAttribute.ServerID:
                                    serverID = node.ReadUInt16();
                                    break;

                                case ItemAttribute.ClientID:
                                    clientID = node.ReadUInt16();
                                    break;

                                case ItemAttribute.GroundSpeed:
                                    node.ReadUInt16();
                                    break;

                                case ItemAttribute.Name:
                                    node.ReadBytes(datalen);
                                    break;

                                case ItemAttribute.SpriteHash:
                                    node.ReadBytes(datalen);
                                    break;

                                case ItemAttribute.MinimaColor:
                                    node.ReadUInt16();
                                    break;

                                case ItemAttribute.MaxReadWriteChars:
                                    node.ReadUInt16();
                                    break;

                                case ItemAttribute.MaxReadChars:
                                    node.ReadUInt16();
                                    break;

                                case ItemAttribute.Light:
                                    node.ReadUInt16();
                                    node.ReadUInt16();
                                    break;

                                case ItemAttribute.StackOrder:
                                    node.ReadByte();
                                    break;

                                case ItemAttribute.TradeAs:
                                    node.ReadUInt16();
                                    break;

                                default:
                                    node.BaseStream.Seek(datalen, SeekOrigin.Current);
                                    break;
                            }
                        }

                        Item item = new Item(serverID, clientID);
                        item.SubType = group == ItemGroup.Splash || group == ItemGroup.Fluid || (flags & ItemFlag.Stackable) == ItemFlag.Stackable;

                        _items.Add(item);
                        node = reader.GetNextNode();
                    } while (node != null);
                }

                OpenLua.IsEnabled = true;
                OpenFolder.IsEnabled = true;
                OtbPath.Text = path;
                Console.WriteLine("[INFO] OTB File loaded: " + path);
            } catch (Exception ex) {
                Console.WriteLine("[ERROR::OTB] \n    Message: " + ex.Message +
                    "\n    Tracer: " + ex.StackTrace);
            }
        }

        #endregion

        #region Xml file
        public bool ParseItemXML(bool isClientID, string path)
        {
            try
            {
                Console.WriteLine("[INFO] Loading XML file, please wait...");
                XmlDocument xml = new XmlDocument();
                xml.Load(path);

                XmlNode itemsNode = xml.LastChild;
                if (itemsNode == null || itemsNode.Name != _itemsNode || itemsNode.ChildNodes.Count == 0) {
                    throw new Exception("'" + _itemsNode + "' node could not be found or is empty.");
                }

                string comment = string.Empty;
                foreach (XmlNode itemNode in itemsNode.ChildNodes) {
                    if (itemNode.NodeType == XmlNodeType.Comment) {
                        continue;
                    } else if (itemNode.Name != _itemNode) {
                        throw new Exception("'" + _itemNode + "' node was expected inside '" + _itemsNode + "', got '" + itemNode.Name + "'.");
                    }

                    string name = string.Empty;
                    ushort id = 0;
                    ushort fromid = 0;
                    ushort toid = 0;
                    foreach (XmlAttribute attribute in itemNode.Attributes) {
                        if (attribute.NodeType == XmlNodeType.Comment) {
                            continue;
                        }
                        
                        string value = attribute.Value;
                        if (value.Length == 0) {
                            continue;
                        }

                        switch (attribute.Name) {
                            case "id": {
                                    id = ushort.Parse(value);
                                    break;
                                }
                            case "fromid": {
                                    fromid = ushort.Parse(value);
                                    break;
                                }
                            case "toid": {
                                    toid = ushort.Parse(value);
                                    break;
                                }
                            case "name": {
                                    name = value;
                                    break;
                                }
                            default: {
                                    continue;
                                }
                        }
                    }

                    if (name.Length > 0) {
                        if (id != 0 && id >= 100) {
                            Item item = GetItemByID(id, isClientID);
                            if (item != null) {
                                item.Name = name;
                            } else {
                                if ((_ConvertFromType == Convert_t.NameAndServerID || _ConvertFromType == Convert_t.ServerID) && _ConvertToType == Convert_t.ServerID) {
                                    Console.WriteLine("[INFO] Item with ID: '" + id + "' does not exist on OTB. Pushing it from xml.");
                                    Item newItem = new Item(id, ushort.MaxValue);
                                    newItem.Name = name;
                                    newItem.Provisory = true;
                                    _items.Add(newItem);
                                } else if ((_ConvertFromType == Convert_t.NameAndClientID || _ConvertFromType == Convert_t.ClientID) && _ConvertToType == Convert_t.ClientID) {
                                    Console.WriteLine("[INFO] Item with ID: '" + id + "' does not exist on OTB. Pushing it from xml.");
                                    Item newItem = new Item(ushort.MaxValue, id);
                                    newItem.Name = name;
                                    newItem.Provisory = true;
                                    _items.Add(newItem);
                                } else {
                                    Console.WriteLine("[INFO] Item with ID: '" + id + "' does not exist on OTB, try updating it.");
                                }
                            }
                        } else if (fromid != 0 && toid != 0 && fromid <= toid && fromid >= 100) {
                            for (ushort itemid = fromid; itemid <= toid; itemid++) {
                                Item item = GetItemByID(itemid, isClientID);
                                if (item != null) {
                                    item.Name = name;
                                } else {
                                    if ((_ConvertFromType == Convert_t.NameAndServerID || _ConvertFromType == Convert_t.ServerID) && _ConvertToType == Convert_t.ServerID) {
                                        Console.WriteLine("[INFO] Item with ID: '" + itemid + "' does not exist on OTB. Pushing it from xml.");
                                        Item newItem = new Item(itemid, ushort.MaxValue);
                                        newItem.Name = name;
                                        newItem.Provisory = true;
                                        _items.Add(newItem);
                                    } else if ((_ConvertFromType == Convert_t.NameAndClientID || _ConvertFromType == Convert_t.ClientID) && _ConvertToType == Convert_t.ClientID) {
                                        Console.WriteLine("[INFO] Item with ID: '" + itemid + "' does not exist on OTB. Pushing it from xml.");
                                        Item newItem = new Item(ushort.MaxValue, itemid);
                                        newItem.Name = name;
                                        newItem.Provisory = true;
                                        _items.Add(newItem);
                                    } else {
                                        Console.WriteLine("[INFO] Item with ID: '" + itemid + "' does not exist on OTB, try updating it.");
                                    }
                                }
                            }
                        }
                    }
                }

                Console.WriteLine("[INFO] XML File loaded: " + path);
            } catch (Exception ex) {
                Console.WriteLine("[ERROR::XML] \n    Message: " + ex.Message +
                    "\n    File: " + path +
                    "\n    Tracer: " + ex.StackTrace);
                return false;
            }

            return true;
        }

        #endregion

        #region Callbacks
        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            if (_worker != null && _worker.IsBusy) {
                return;
            }

            string fromText = ((TextBlock)FromType.SelectedItem).Text;
            if (fromText == "ServerID") {
                _ConvertFromType = Convert_t.ServerID;
            } else if (fromText == "ClientID") {
                _ConvertFromType = Convert_t.ClientID;
            } else if (fromText == "Name/ServerID") {
                _ConvertFromType = Convert_t.NameAndServerID;
            } else if (fromText == "Name/ClientID") {
                _ConvertFromType = Convert_t.NameAndClientID;
            } else {
                MessageBox.Show("Unknown conversion type 'From'. (" + fromText + ")", "Conversion type error", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string toText = ((TextBlock)ToType.SelectedItem).Text;
            if (toText == "ServerID") {
                _ConvertToType = Convert_t.ServerID;
            } else if (toText == "ClientID") {
                _ConvertToType = Convert_t.ClientID;
            } else {
                MessageBox.Show("Unknown conversion type 'To'. (" + toText + ")", "Conversion type error", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (_ConvertFromType == _ConvertToType) {
                MessageBox.Show("You have selected the same conversion type for input and output.", "Conversion type error", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            FileDialog xmlDialog = new OpenFileDialog
            {
                Filter = "XML file (*.xml)|*.xml",
                Title = "Open XML File"
            };

            if ((bool)xmlDialog.ShowDialog()) {
                _xmlPath = xmlDialog.FileName;
            } else {
                MessageBox.Show("Monster converter has failed to load your XML file, please try load it again or choose another file", "Conversion error", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            Convert.Content = "Converting";
            Convert.IsEnabled = false;

            ProgressText.Text = "Files loaded: 0";
            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.ProgressChanged += WorkerChanged;
            _worker.RunWorkerCompleted += WorkerCompleted;

            _progress = 0;

            if (_isFolder) {
                Console.WriteLine("[INFO] Converting folders and subfolders.");
                _worker.DoWork += WorkerFolderPath;
            } else {
                Console.WriteLine("[INFO] Converting file.");
                _worker.DoWork += WorkerFilePath;
            }

            if (_worker.IsBusy != true) {
                _worker.RunWorkerAsync();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("[INFO] Closing application");
            Application.Current.Shutdown();
        }

        private void Log_Click(object sender, RoutedEventArgs e)
        {
            AllocConsole();

            Console.WriteLine("### Monster created to work on OpenTibiaBR - Canary Project. ###");
            Console.WriteLine("-> Check the Canary project: https://github.com/opentibiabr/canary");
            Console.WriteLine("-> OpenTibiaBR github link: https://github.com/opentibiabr/otservbr-global");
            Console.WriteLine("-> OpenTibiaBR forum: https://forums.otserv.com.br/");
            Console.WriteLine("-> Converter author github link: https://github.com/marcosvf132");
            Console.WriteLine("");
            Console.WriteLine("* Closing the log will terminate the application.");
            LogButton.IsEnabled = false;
        }
        
        public void Otb_Click(object sender, RoutedEventArgs e)
        {
            FileDialog dialog = new OpenFileDialog
            {
                Filter = "OTB files (*.otb)|*.otb",
                Title = "Open OTB File"
            };

            if ((bool)dialog.ShowDialog()) {
                ParseItemOtb(dialog.FileName);
            }

        }

        public void LuaFile_Click(object sender, RoutedEventArgs e)
        {
            FileDialog dialog = new OpenFileDialog
            {
                Filter = "LUA file (*.lua)|*.lua",
                Title = "Open LUA File"
            };

            if ((bool)dialog.ShowDialog()) {
                _isFolder = false;
                _path = dialog.FileName;
                Convert.IsEnabled = true;
                OtbmPath.Text = _path;
            }
        }

        public void LuaPath_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Open lua monsters path",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select directory"
            };

            if ((bool)dialog.ShowDialog()) {
                _isFolder = true;
                _path = Path.GetDirectoryName(dialog.FileName);
                Convert.IsEnabled = true;
                OtbmPath.Text = _path;
            }
        }

        #endregion

        #region Async worker
        private void WorkerFilePath(object sender, DoWorkEventArgs e)
        {
            // Loading items.xml to find items name
            if (ParseItemXML(_ConvertFromType == Convert_t.NameAndClientID || _ConvertFromType == Convert_t.ClientID, _xmlPath)) {

                // Read the file
                ParseLuaAsBinary(_path);

                // Cleaning provisory items.
                // Provisory items are created on special occasions to prevent problems when parsing items on XML.
                ClearProvisoryItems();
            }
        }

        private void WorkerFolderPath(object sender, DoWorkEventArgs e)
        {
            // Loading items.xml to find items name
            if (ParseItemXML(_ConvertFromType == Convert_t.NameAndClientID || _ConvertFromType == Convert_t.ClientID, _xmlPath)) {

                // Read folder and subfolder
                ReadLuaFolderSubFolder(_path);

                // Cleaning provisory items.
                // Provisory items are created on special occasions to prevent problems when parsing items on XML.
                ClearProvisoryItems();
            }
        }

        private void WorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Convert.Content = "Convert";
            Convert.IsEnabled = true;
        }

        private void WorkerChanged(object sender, ProgressChangedEventArgs e)
        {
            ProgressText.Text = "Files loaded: " + e.ProgressPercentage;
        }

        #endregion

        public MainWindow()
        {
            InitializeComponent();
        }

    }
}
