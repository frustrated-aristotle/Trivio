const nickList = [
    // General & Cool Names
    "TheNightLord",
    "UnknoWn_X",
    "BluePhantom",
    "PolarisStar",
    "SilentWarrior",
    "DigitalDrifter",
    "ShadowDancer",
    "TitanRising",
    "ChronoKeeper",
    "ComplexMind",

    // Humorous & Witty Names
    "DebuggingGuy",
    "CoffeeBrain",
    "SleepyPanda",
    "LagLord",
    "Error404",
    "AnonymousPotato",
    "PixelFighter",
    "KeyboardDancer",
    "ArtisticApe",
    "JustHereToWin",

    // Trivia & Knowledge Themed
    "KnowledgeBeast",
    "RandomFact",
    "QuestionMark",
    "TheEncyclopedia",
    "ProfessorX",
    "MemoryPower",
    "TimeMachine",
    "SpeedDemon",
    "TheFinalAnswer",
    "TriviaKing",

    // Food & Drink Themed
    "ToasterMaster",
    "ColdEspresso",
    "SpicyChili",
    "SweetOrange",
    "LatteMaster",
    "ExtraCheese",
    "HotdogKing",
    "LemonZest",
    "FreshMint",
    "SalsaQueen",

    // Animal & Nature Themed
    "SlyFox",
    "FlyingTiger",
    "NightOwl",
    "SilentWolf",
    "IcyRiver",
    "CrazyHawk",
    "LastSamurai",
    "SolarFlare",
    "RedScorpion",
    "ForestSpirit",

    // Tech & Code References
    "ZeroOne",
    "HexCode",
    "BitBrain",
    "InfinityLoop",
    "StackOverflower",
    "TheBuffer",
    "SyntaxError",
    "LambdaLife",
    "RootAccess",
    "NotTheAdmin",
    "CodeJockey",
    "API_Hunter",

    // Other Fun Names
    "IClickedFirst",
    "MeAgain",
    "JustCurious",
    "SecretFormula",
    "GameIsOver",
    "NeverCame",
    "ShortCircuit",
    "MindBlown",
    "ReplayPls",
    "LuckyDice"
];

function bindRandomNameButtons() {
    const buttons = document.querySelectorAll('#randomUsernameBtn, #randomAttendUsernameBtn');
    buttons.forEach(function(button){
        button.addEventListener('click', function (e) {
            e.preventDefault();
            const targetId = button.getAttribute('data-target');
            if (!targetId) {
                console.warn('Random name button missing data-target');
                return;
            }
            const input = document.getElementById(targetId);
            if (!input) {
                console.warn('Random name target not found: ' + targetId);
                return;
            }
            const randomIndex = Math.floor(Math.random() * nickList.length);
            const randomName = nickList[randomIndex];
            input.value = randomName;
        });
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', bindRandomNameButtons);
} else {
    bindRandomNameButtons();
}