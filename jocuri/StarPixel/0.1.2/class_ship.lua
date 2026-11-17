--CLASS: SHIP
---@param self table
---@param x integer
---@param y integer
---@param xsec integer
---@param ysec integer
---@param faction string
---@param speed integer
---@param NPC boolean
function new_ship(self, x, y, xsec, ysec, faction, speed, NPC)
    self.x = x
    self.y = y
    self.xsec = xsec
    self.ysec = ysec
    self.faction = faction
    self.speed = speed
    self.NPC = NPC

    function self.update(dt)
        --SHIP MOVEMENT
        if (self.NPC == false) then
            if (love.keyboard.isDown("w") == true) then
                self.y = self.y - self.speed
            end
            if (love.keyboard.isDown("s") == true) then
                self.y = self.y + self.speed
            end
            if (love.keyboard.isDown("a") == true) then
                self.x = self.x - self.speed
            end
            if (love.keyboard.isDown("d") == true) then
                self.x = self.x + self.speed
            end
        end
        --SECTOR SWITCHING
        if (self.x > 1000) then
            self.xsec = self.xsec + 1
            self.x = 0
            if (self.NPC == false and mapKey == false) then
                stars(200)
            end
        end
        if (self.x < 0) then
            self.xsec = self.xsec - 1
            self.x = 999
            if (self.NPC == false and mapKey == false) then
                stars(200)
            end
        end
        if (self.y > 1000) then
            self.ysec = self.ysec + 1
            self.y = 0
            if (self.NPC == false and mapKey == false) then
                stars(200)
            end
        end
        if (self.y < 0) then
            self.ysec = self.ysec - 1
            self.y = 999
            if (self.NPC == false and mapKey == false) then
                stars(200)
            end
        end
    end

    function self.draw()
        --IMAGE GET_RES
        local imageW = shipIMG:getWidth()
        local imageH = shipIMG:getHeight()

        --PIXEL COORDS
        local xPIXEL = unit * self.x
        local yPIXEL = unit * self.y




        if mapKey then
            --MAP VIEW
            love.graphics.setColor(0, 0.5, 0, 0.3)
            love.graphics.rectangle("fill", self.xsec * (minRES / xmap) + wCrct,
                self.ysec * (minRES / ymap) + hCrct, (minRES / xmap) - 1, (minRES / ymap) - 1)
            love.graphics.setColor(0, 1, 0)

            love.graphics.draw(mapF1IMG,
                (xPIXEL + self.xsec * minRES) / xmap + wCrct - imageW * unit / 2,
                (yPIXEL + self.ysec * minRES) / ymap + hCrct - imageH * unit, 0,
                unit, unit)
            love.graphics.setColor(1, 1, 1)
        else
            --SECTOR VIEW
            love.graphics.draw(shipIMG,
                xPIXEL - imageW * unit * 2 + wCrct,
                yPIXEL - imageH * unit * 2 + hCrct, 0,
                unit * 4, unit * 4)
        end
    end
end

return new_ship
