function GAME_switch()
    stage = "game"
end

function MENU_init()
    butt_newgame = {}
    new_button(butt_newgame, 200, 200, 300, 50, "NEW GAME", GAME_switch)
    butt_loadgame = {}
    new_button(butt_loadgame, 200, 300, 300, 50, "LOAD GAME", os.exit)
    butt_settings = {}
    new_button(butt_settings, 200, 400, 300, 50, "SETTINGS", os.exit)
    butt_quit = {}
    new_button(butt_quit, 200, 500, 300, 50, "QUIT", os.exit)
end

function MENU()
    butt_quit.draw()
    butt_settings.draw()
    butt_loadgame.draw()
    butt_newgame.draw()
end
