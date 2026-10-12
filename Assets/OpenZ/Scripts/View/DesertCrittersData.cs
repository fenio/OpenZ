using UnityEngine;

namespace OpenZ
{
    public static class DesertCrittersData
    {
        /// <summary>Burrows (rabbits) and dens (snakes): x, z.</summary>
        public static readonly Vector2[] Homes = { new Vector2(113.5f, -24.5f), new Vector2(111.5f, -111.5f), new Vector2(100.5f, -77.5f), new Vector2(31.5f, -131.5f), new Vector2(32.5f, -85.5f), new Vector2(112.5f, -48.5f), new Vector2(28.5f, -161.5f) };
        public static readonly int[] HomeCount = { 2, 2, 2, 1, 1, 1, 1 };
        public static readonly bool[] HomeIsSnake = { false, false, false, true, true, true, true };

        /// <summary>Roam mask, 1 m cells: bit (j * Width + i), i = floor(x), j = floor(-z); 13523 of 25920 cells.</summary>
        public const int Width = 144, Height = 180;
        public const string MaskB64 =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADgAAAAAAAAAAAAAAAAAAAAAAHwAAAAAAAAAAAAAA" +
            "AADgHwDAPwAAAAAfAADgAwAAAIDwPwDgHwAAAID/DwDwBwAAAMDhfwDgjzwMcPj/fxzgDwAAAODAPwDAz38e+Pz//37AHwAAAODADwAA/z8cfPj/8//ADwAA" +
            "AODADwAA/z88fPj/4f/AAwAAAODADwAA/zz8//z/wP/AAQAAAODhDwAA/zz8////zP/hAQAAAOD/DwAA////////////AQAAAMD/DwAA////////////AQAA" +
            "AMD/BwAA/v//////////AwAAQMD/DwAA////////////AwAA4MD/BwAA/v////////P/AwAA4OH/BwAA/v////////P/AQAA4P//BwAA/v//////////AQAA" +
            "4D/ABwAA/v//////////AQAAwB+ABwAAPv//////////+wAAAB6ABwAAPv///////////QEAAB6ADwAA//////8//////AAAAB6ADwAA//////8f/////AAA" +
            "AD7ADwAA//////+P/////wAAADzADwAA//9//P/P4f///wAAADjADwAA//8/+P//wP/w/x8AABCADwAA//8/+P9/gP/g/z8AAACADwAA//8/+P9/gP/A/z8A" +
            "ABCA/wME//8/vN9/gP+A/x8AALjf//8Pz/8/HIB/gP8A/x8AAPj///8Pz/8/HID/wP8A/j8AAPD///////8/HID/4f8A/D8AAPD///////8/HID///8A+D8A" +
            "APD///zz//8/HMDP//8AMBAAAPj///zz//8/HMDP//8AAAAAAPj///////8/POOf//8BAAAAAPj///////8/+OcP8P8DAAAAAPj///////w/+MMH4P8HAAAA" +
            "APj///////w/+MMP8P8PIAAAAPj///////8/+MMP8P8fcAAAAPD///////8/+MMP8P8/8BAAAMD8D/////8f+P8P8P///z8AAMD4D/////8f+P8H4P///z8A" +
            "APDxD/////8f/P8P8P///z8AAPjzD/////8f+P8P8P///z8AAPj//wP8//8f8P8P8P///z8AAPj//wPw//8P8P8P8P///z8AAPD//wHg//MA8P///////z8A" +
            "AAA+AAAAAAAA4P///////z8AAAAAAAAAAAAAAP7//////38AAAAAAAAAAAAAAP7///////8AAAAAAAAAAAAAAP////////8AAAAAAAAAAAAAgP8f/v///38A" +
            "AIAPwAEAgAAAgP8P/P///z8AAPD//wMA+P8P8P8H+P///z8AAPj//wPg//8f+MMH+P8PPBAAAPj//wPw//8f/MEH+P8HGAAAADj///////8f/PAH+P8DAAAA" +
            "ADj///////8f/PgP/P8BAAAAAPj///////8f/PEf/v8AAAAAAPD//////z8f+PP///8AAAAAAOD//////x8O+Pn///8AAAAAAMD///9/+B8AMPD///8AAAAA" +
            "AAAA/P8/8A8AAAD///8AAAAAAAAA+P8fYAAAAAD///8AAAMAAAAA8P8fAAAA/h////8A/gcAAAAA8P8fAMAD/z////8A/w8AAAAA8P8fIOAH/j////+B/w8A" +
            "AAAA8P8/cOAD/D/////D/w8AAOQH8P9/eOAB+D/////D/w8AAP8P8P//f/AD/D////+D/w8AgP8f8P//f/gD/D////8DPAQAgP8/8P//f/gD/B/+//8DOAAA" +
            "wP8/8P//f/gDPB/+//8HEAAA4P8/8P////gBOA/+//8PIAwA4P8/8D//f/gD/A/8//8P8H4A4P8/8D//f/gD/A/8//8P8P8A4P8/8P//f/gD/A/8//8P8PwA" +
            "4P8/8P//f/gD/A/+//8P8PgA4MM/+P//f/j//x/+//8f+EEA4MM//P//P/D//w/8//8//AMAwMP/////D8D//wPw/////wMAgIP/////D8D//wPw/////8MA" +
            "gB//////D8D//wPw/z////8BgD//////D8D//wPw/z////8BgP//////D8D//wPw//////8AgP//////D8D//wPw/////z8AgP//////D8D//wPw////wz8A" +
            "gP//////D8D//wPw////gT8AAP8P/P//H+D//wf4////AD8AAP8P+P//D/Af8A/8////AD4AAA4PwP//D/gf4A/+////ABAAAA4PgP//H/gf4A/+///hAAAA" +
            "AA4PAP//P/AP4B/+//9AAAAAAAQPAP//fwAA4B/+/38AAAAAAPAfAP//fwAAwA/+/38AAAAAAPg/AP//fwAAAAD8/38AAAAAAPj/Af9/fwAAAAD4/3+ABwAA" +
            "APD/A/9///8PAAD4///ADwAAAOD/B//z//8fAMD4///hfwAAAOD/D//z//8fAPj//////wAAAOD/D/////8cAPz///9/+AMAAOD/D/////8cAPj///8/8AcA" +
            "AOD/D//P//8fAPD///8/8AcAAOD/B//P//8fAOD///8/8AMAAMA/AP////wPAOD///8/cAAAAOAfAP////gPAOD///9/OAAAAOAAAP////EPAOD/////PwAA" +
            "AOAAAP////MfAPD/////PwAAAOAAAP//P/8fAPj/PwD/PwAAAOAAAP//P/8fAPz/PwD/fwAAAOAA4P///z8fAPj/HwD+PwAAAOAA8P///z8PAOD/DwAEAAAA" +
            "AOD///////8HAAAAAAAAAAAAAOD///8P8P8HAAAAAAAAAAAAAOD///8H4P8HAAAAAAAAAAAAAPj///8P8P8HAAAAAAAAAAAAAP////8P8P8PAAD8HwD+DwMA" +
            "gP////8P8P8P8P//PwD//wcAgP////8P8A8f+P//PwD//w8AgP////8H4A8f+P//PwD+/x8AgP////8P8A8f+PP//x/w/x8AgP////8P8A8P+PP//3/w/w8A" +
            "AP////8P8P8P+D/////w/wcAAPj///8P8P8P+B/////w/wMAADjA//////8f+I///////wcAADiA//////8f+M///////w8AABAA/P////wf+P////P//w8A" +
            "AAAA/P////w/+P////P//w8AAAAA/P////8/+P///////wcAAAAA+P//v9w//P///////w8AAEAA8P//H4A//P/P/////w8AAOAA8P//H4A//P/P/////z8A" +
            "AOAH8P//H4A//P///////38AAOAP4P//H4A//P//////A/wAAPAPwP//H8A/+P//////AfgAAPAPgP//H8Af+P///z/8AXgAAPB/AP//P/8f+D//AADwAfgA" +
            "APj/AP////8f+D//AADwAfgAAPj/AP////8f+P//AADwA3wAADhAAP////8f+P//AADwA/wAABAAAf////8f+P//AADwA/wBAACAA/////8f+P//AADwAfgB" +
            "AACAB/////8//P//AADwAfgAAACAD///////////AADwAfgAAADAD/////////98AADg+/0BAADAD/////////98AADg//8DAADADz/88/////9/AADg//8D" +
            "AACADz/88/////9/AADg//8BAACADz/8//////9/AADg//8BAACADz/8//////9/AADg//8BAIDf////////////AADw/+EBAMD/////////////AADw/8AD" +
            "AOD/////////P/x/AADg/8ADAOD///////9/OPz/AADw/8AHAPDP//////8/OPz/AADw4cAPAPjP//////8/OPz/AADwwOEfAPj///////8/+P//IEDwwP8f" +
            "APj/+P////8/+P//////wP8fAPD/wP///z8f8P//////wP8fAAD+gP///x8e4P//////4f8PAAD8gf///w8MwP//////w/0HAAD48f///wAAgP//////gyAA" +
            "AAD4////PwAAAPPH/5//AQAAAAD4//9/HAAAAAAAAAAAAAAAAADwf/4/AAAAAAAAAAAAAAAAAADAP8gHAAAAAAAAAAAAAAAAAACAHwAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    }
}
