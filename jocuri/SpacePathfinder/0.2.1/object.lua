function new_object(self, vtx, scale, translate, rotate)
    self.vtx = vtx
    self.scale = scale
    self.translate = translate
    self.rotate = rotate
    self.vtx_trans = {}

    function self.update()
        local cz = math.cos(self.rotate.z)
        local sz = math.sin(self.rotate.z)

        for i = 1, #self.vtx do
            self.vtx_trans[i] = {}
            self.vtx_trans[i][1] = {}
            self.vtx_trans[i][2] = {}
            self.vtx_trans[i][1].x = self.vtx[i][1].x * self.scale + self.translate.x
            self.vtx_trans[i][1].z = self.vtx[i][1].z * self.scale + self.translate.z
            self.vtx_trans[i][2].x = self.vtx[i][2].x * self.scale + self.translate.x
            self.vtx_trans[i][2].z = self.vtx[i][2].z * self.scale + self.translate.z

            x1 = self.vtx_trans[i][1].x
            z1 = self.vtx_trans[i][1].z
            x2 = self.vtx_trans[i][2].x
            z2 = self.vtx_trans[i][2].z

            self.vtx_trans[i].x = cz * x1 + sz * z1
            self.vtx_trans[i].z = -sz * x1 + cz * z1
            self.vtx_trans[i].x = cz * x2 + sz * z2
            self.vtx_trans[i].z = -sz * x2 + cz * z2
        end

        for i = 1, #self.vtx do
            if self.vtx_trans[i][1].z > 0 and self.vtx_trans[i][2].z > 0 then
                love.graphics.polygon("fill",
                    self.vtx_trans[i][1].x * height / 2 / self.vtx_trans[i][1].z + width / 2,
                    0.5 * height / 2 / self.vtx_trans[i][1].z + height / 2,
                    self.vtx_trans[i][2].x * height / 2 / self.vtx_trans[i][2].z + width / 2,
                    0.5 * height / 2 / self.vtx_trans[i][2].z + height / 2,
                    self.vtx_trans[i][2].x * height / 2 / self.vtx_trans[i][2].z + width / 2,
                    -0.5 * height / 2 / self.vtx_trans[i][2].z + height / 2,
                    self.vtx_trans[i][1].x * height / 2 / self.vtx_trans[i][1].z + width / 2,
                    -0.5 * height / 2 / self.vtx_trans[i][1].z + height / 2
                )
            end
        end
    end
end
