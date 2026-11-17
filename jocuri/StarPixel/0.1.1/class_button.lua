--CLASS: BUTTON
---@param self table
---@param x integer
---@param y integer
---@param Wsize integer
---@param Hsize integer
---@param Text string
---@param func function
function new_button(self, x, y, Wsize, Hsize, Text, func)
    self.x = x
    self.y = y
    self.Wsize = Wsize
    self.Hsize = Hsize
    self.Text = Text
    self.pressed = false
    self.slct = false
    self.func = func

    function self.mousepressed(mouse_butt)
        if (mouse_butt == 1 and self.slct == true) then
            self.pressed = true
        end
    end

    function self.mousereleased(mouse_butt)
        if (mouse_butt == 1) then
            self.pressed = false
        end
    end

    function self.draw()
        local imageW = buttonIMG:getWidth()  --image width
        local imageH = buttonIMG:getHeight() --image height
        local xPIXEL = unit * self.x + wCrct --theoretical pos x transformation to pixels
        local yPIXEL = unit * self.y + hCrct --theoretical pos y transformation to pixels
        local wPIXEL = unit * self.Wsize     --theoretical width transformation to pixels
        local hPIXEL = unit * self.Hsize     --theoretical height transformation to pixels
        local xMouse = love.mouse.getX()     --get mouse x coordonate
        local yMouse = love.mouse.getY()     --get mouse y coordonate
        self.slct = false                    --selection reset
        --MOUSE SELECT
        if (xMouse > xPIXEL and xMouse < xPIXEL + wPIXEL and yMouse > yPIXEL and yMouse < yPIXEL + hPIXEL) then
            love.graphics.rectangle("line", xPIXEL - 2, yPIXEL - 2, wPIXEL + 4, hPIXEL + 4)
            self.slct = true
        end
        --DRAWS BUTTON
        love.graphics.setColor(0, 0.5, 1, 1)
        love.graphics.draw(buttonIMG, xPIXEL, yPIXEL, 0, wPIXEL / imageW, hPIXEL / imageH)

        if self.pressed then
            love.graphics.setColor(0, 0.3, 0.8, 1)
            love.graphics.draw(buttonIMG, xPIXEL + unit * self.Wsize, yPIXEL + unit * self.Hsize, math.pi,
                wPIXEL / imageW, hPIXEL / imageH)
            self.func()
        end

        love.graphics.setColor(0, 0, 0, 1)
        if self.pressed then
            love.graphics.printf(self.Text, xPIXEL + wPIXEL / imageW * unit, yPIXEL + hPIXEL / imageH * unit,
                self.Wsize / 3, "center",
                0,
                unit * 3, unit * 3)
        else
            love.graphics.printf(self.Text, xPIXEL, yPIXEL, self.Wsize / 3, "center", 0,
                unit * 3, unit * 3)
        end
        love.graphics.setColor(1, 1, 1)
    end
end
