function stars(Nbr)
    ---@diagnostic disable-next-line: undefined-field
    cnvs:release()
    cnvs = nil
    cnvs = love.graphics.newCanvas(minRES, minRES)
    love.graphics.setCanvas(cnvs)
    love.graphics.setColor(0, 0, 0, 1)
    love.graphics.rectangle("fill", 0, 0, minRES, minRES)
    love.graphics.setColor(1, 1, 1, 1)
    love.math.setRandomSeed(player_ship.xsec + player_ship.ysec * 10)
    for i = 0, Nbr, 1 do
        love.graphics.points(love.math.random(0, minRES), love.math.random(0, minRES))
    end
    love.graphics.setCanvas()
end

function mapdisplay()
    local imageH, imageV

    ---@diagnostic disable-next-line: undefined-field
    cnvs:release()
    cnvs = nil
    cnvs = love.graphics.newCanvas(minRES, minRES)
    love.graphics.setCanvas(cnvs)

    love.graphics.setColor(1, 1, 1)
    imageH = backgroundIMG:getWidth()
    imageV = backgroundIMG:getHeight()
    love.graphics.draw(backgroundIMG, 0, 0, 0, minRES / imageH, minRES / imageV)
    love.graphics.setColor(0, 0.5, 1)
    for yi = 0, ymap - 1 do
        for xi = 0, xmap - 1 do
            love.graphics.rectangle("line", xi * (minRES / xmap), yi * (minRES / ymap), (minRES / xmap) - 1,
                (minRES / ymap) - 1)
        end
    end

    love.graphics.setCanvas()
end
