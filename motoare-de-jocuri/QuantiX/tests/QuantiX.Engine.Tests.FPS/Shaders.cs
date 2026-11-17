namespace QuantiX.Engine.Tests.FPS;

/// <summary>
/// Centralized GLSL shader sources for all rendering pipelines.
/// </summary>
public static class Shaders
{
  // ── Sky (from voxel game, fullscreen triangle) ──────────────────────
  public const string SkyVert =
    """
    #version 330 core
    layout(location = 0) in vec2 aPos;
    out vec2 vPos;
    void main()
    {
      vPos = aPos;
      gl_Position = vec4(aPos, 1.0, 1.0);
    }
    """;

  public const string SkyFrag =
    """
    #version 330 core
    in vec2 vPos;
    out vec4 FragColor;

    layout(std140) uniform SkyData {
      vec4 uSkyParams;   // x=timeOfDay, y=cloudOffset, z=tanHalfFov, w=aspect
      vec4 uCamRight;
      vec4 uCamUp;
      vec4 uCamForward;
    };

    float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

    float noise(vec2 p)
    {
      vec2 i = floor(p), f = fract(p);
      f = f * f * (3.0 - 2.0 * f);
      return mix(mix(hash(i), hash(i+vec2(1,0)), f.x),
                 mix(hash(i+vec2(0,1)), hash(i+vec2(1,1)), f.x), f.y);
    }

    float fbm(vec2 p)
    {
      float v=0.0, a=0.5;
      for(int i=0;i<4;i++){v+=a*noise(p);p*=2.0;a*=0.5;}
      return v;
    }

    void main()
    {
      float timeOfDay=uSkyParams.x, cloudOff=uSkyParams.y, tanFov=uSkyParams.z, aspect=uSkyParams.w;
      vec3 dir = normalize(uCamForward.xyz + uCamRight.xyz*vPos.x*tanFov*aspect + uCamUp.xyz*vPos.y*tanFov);
      float sunAngle = timeOfDay*6.28318;
      vec3 sunDir = normalize(vec3(cos(sunAngle), sin(sunAngle), 0.3));
      float dayFactor = smoothstep(-0.1, 0.2, sunDir.y);
      float h = clamp(dir.y*0.8+0.5, 0.0, 1.0);
      vec3 dayZenith=vec3(0.25,0.47,0.92), dayHorizon=vec3(0.60,0.75,0.95);
      vec3 nightZenith=vec3(0.01,0.01,0.05), nightHorizon=vec3(0.04,0.04,0.12);
      vec3 sky = mix(mix(nightHorizon,nightZenith,h), mix(dayHorizon,dayZenith,h), dayFactor);

      float sunsetFactor = smoothstep(-0.15,0.0,sunDir.y)*smoothstep(0.3,0.0,sunDir.y);
      float horizonGlow = pow(1.0-abs(dir.y), 4.0);
      vec3 sunsetColor = mix(vec3(1.0,0.3,0.1), vec3(1.0,0.6,0.2), h);
      sky = mix(sky, sunsetColor, sunsetFactor*horizonGlow*0.8);

      float sunDot = dot(dir, sunDir);
      float sunDisk = smoothstep(0.998,0.999,sunDot);
      float sunGlow = pow(max(sunDot,0.0),64.0)*0.4;
      vec3 sunColor = mix(vec3(1.0,0.8,0.4),vec3(1.0,0.95,0.8),dayFactor);
      sky += sunColor*(sunDisk+sunGlow)*step(-0.1,sunDir.y);

      vec3 moonDir = -sunDir;
      float moonDot = dot(dir, moonDir);
      sky += vec3(0.8,0.85,0.95)*(smoothstep(0.998,0.9995,moonDot)+pow(max(moonDot,0.0),128.0)*0.2)*(1.0-dayFactor);

      if(dayFactor<0.5 && dir.y>0.0){
        vec2 starUV = vec2(atan(dir.z,dir.x),asin(dir.y))*10.0;
        float star = hash(floor(starUV));
        star = step(0.97,star)*(1.0-dayFactor*2.0)*(.5+.5*sin(star*100.0+cloudOff*50.0));
        sky += vec3(star*dir.y);
      }

      if(dir.y>0.02){
        vec2 cloudUV = dir.xz/(dir.y+0.1)*0.3;
        cloudUV.x += cloudOff;
        float cloud = smoothstep(0.35,0.65,fbm(cloudUV*4.0));
        vec3 cc = mix(vec3(0.05,0.06,0.1),vec3(1.0),dayFactor);
        cc = mix(cc,vec3(1.0,0.6,0.3),sunsetFactor*0.5);
        sky = mix(sky, cc, cloud*0.7*smoothstep(0.02,0.15,dir.y));
      }

      if(dir.y<0.0){
        float bf = smoothstep(0.0,-0.3,dir.y);
        sky = mix(sky, mix(dayHorizon,nightHorizon,1.0-dayFactor)*0.4, bf);
      }

      FragColor = vec4(clamp(sky,0.0,1.0), 1.0);
    }
    """;

  // ── World / Object (Blinn-Phong with fog and AO) ───────────────────
  public const string WorldVert =
    """
    #version 330 core
    layout(location = 0) in vec3 aPos;
    layout(location = 1) in vec3 aNormal;
    layout(location = 2) in vec2 aTexCoord;
    layout(location = 3) in float aAO;

    layout(std140) uniform WorldData {
      mat4 uMVP;
      mat4 uModel;
      vec4 uLighting;   // x=ambient, y=sunY, z=specPower, w=tint(packed)
      vec4 uCamPos;     // xyz=camera position
      vec4 uFog;        // x=fogStart, y=fogEnd, z=0, w=0
    };

    out vec3 vWorldPos;
    out vec3 vNormal;
    out vec2 vTexCoord;
    out float vAO;
    out float vFogFactor;

    void main()
    {
      vec4 worldPos = uModel * vec4(aPos, 1.0);
      vWorldPos = worldPos.xyz;
      vNormal = mat3(uModel) * aNormal;
      vTexCoord = aTexCoord;
      vAO = aAO;

      float dist = length(worldPos.xyz - uCamPos.xyz);
      vFogFactor = clamp((dist - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0);

      gl_Position = uMVP * vec4(aPos, 1.0);
    }
    """;

  public const string WorldFrag =
    """
    #version 330 core
    in vec3 vWorldPos;
    in vec3 vNormal;
    in vec2 vTexCoord;
    in float vAO;
    in float vFogFactor;
    out vec4 FragColor;

    layout(std140) uniform WorldData {
      mat4 uMVP;
      mat4 uModel;
      vec4 uLighting;
      vec4 uCamPos;
      vec4 uFog;
    };

    uniform sampler2D uTexture;

    void main()
    {
      vec3 N = normalize(vNormal);
      float ambient = uLighting.x;

      // Sun direction (matches sky)
      float sunAngle = uLighting.y * 6.28318;
      vec3 sunDir = normalize(vec3(cos(sunAngle), sin(sunAngle), 0.3));
      float dayFactor = smoothstep(-0.1, 0.2, sunDir.y);

      // Diffuse
      float diff = max(dot(N, sunDir), 0.0) * dayFactor;

      // Specular (Blinn-Phong)
      vec3 viewDir = normalize(uCamPos.xyz - vWorldPos);
      vec3 halfDir = normalize(sunDir + viewDir);
      float spec = pow(max(dot(N, halfDir), 0.0), uLighting.z) * dayFactor * 0.5;

      // Fake ground shadow (darken fragments near ground that face up)
      float groundShadow = 1.0;
      if (vWorldPos.y < 0.1 && N.y > 0.5)
        groundShadow = 0.7;

      vec4 tex = texture(uTexture, vTexCoord);

      // Color tint from uniform (packed as float: r*256*256 + g*256 + b)
      float tintPacked = uLighting.w;
      vec3 tint = vec3(1.0);
      if (tintPacked > 0.0)
      {
        tint.r = floor(tintPacked / 65536.0) / 255.0;
        tint.g = floor(mod(tintPacked, 65536.0) / 256.0) / 255.0;
        tint.b = mod(tintPacked, 256.0) / 255.0;
      }

      vec3 lit = tex.rgb * tint * (ambient + diff) * vAO * groundShadow + vec3(spec);

      // Fog (blend to sky horizon color)
      vec3 fogColor = mix(vec3(0.6,0.75,0.95), vec3(0.04,0.04,0.12), 1.0-dayFactor);
      lit = mix(lit, fogColor, vFogFactor);

      FragColor = vec4(lit, tex.a);
    }
    """;

  // ── Grass (wind-animated billboards) ────────────────────────────────
  public const string GrassVert =
    """
    #version 330 core
    layout(location = 0) in vec3 aPos;
    layout(location = 1) in vec2 aTexCoord;
    layout(location = 2) in vec2 aGrassData; // x=windPhase, y=height01

    layout(std140) uniform GrassData {
      mat4 uVP;
      vec4 uWind;     // x=time, y=strength, z=0, w=0
      vec4 uCamPos;
    };

    out vec2 vTexCoord;
    out float vHeight;
    out float vFogFactor;

    void main()
    {
      vec3 pos = aPos;

      // Wind: offset top vertices
      float windOffset = sin(uWind.x * 2.0 + aGrassData.x * 6.28318) * uWind.y * aGrassData.y;
      pos.x += windOffset;
      pos.z += windOffset * 0.5;

      vTexCoord = aTexCoord;
      vHeight = aGrassData.y;

      float dist = length(pos - uCamPos.xyz);
      vFogFactor = clamp((dist - 60.0) / 80.0, 0.0, 1.0);

      gl_Position = uVP * vec4(pos, 1.0);
    }
    """;

  public const string GrassFrag =
    """
    #version 330 core
    in vec2 vTexCoord;
    in float vHeight;
    in float vFogFactor;
    out vec4 FragColor;

    layout(std140) uniform GrassData {
      mat4 uVP;
      vec4 uWind;
      vec4 uCamPos;
    };

    void main()
    {
      // Grass color gradient (darker at base, lighter at tip)
      vec3 baseColor = vec3(0.15, 0.35, 0.08);
      vec3 tipColor = vec3(0.3, 0.55, 0.15);
      vec3 color = mix(baseColor, tipColor, vHeight);

      // Alpha cutout for blade shape
      float centerDist = abs(vTexCoord.x - 0.5) * 2.0;
      float bladeShape = 1.0 - smoothstep(0.3 - vHeight * 0.2, 0.5, centerDist);
      if (bladeShape < 0.1) discard;

      // Fog
      vec3 fogColor = vec3(0.6, 0.75, 0.95);
      color = mix(color, fogColor, vFogFactor);

      FragColor = vec4(color, bladeShape);
    }
    """;

  // ── Particles (billboarded, colored) ────────────────────────────────
  public const string ParticleVert =
    """
    #version 330 core
    layout(location = 0) in vec3 aPos;
    layout(location = 1) in vec2 aTexCoord;
    layout(location = 2) in vec4 aColor;

    layout(std140) uniform ParticleData {
      mat4 uVP;
    };

    out vec2 vTexCoord;
    out vec4 vColor;

    void main()
    {
      vTexCoord = aTexCoord;
      vColor = aColor;
      gl_Position = uVP * vec4(aPos, 1.0);
    }
    """;

  public const string ParticleFrag =
    """
    #version 330 core
    in vec2 vTexCoord;
    in vec4 vColor;
    out vec4 FragColor;

    void main()
    {
      float dist = length(vTexCoord - vec2(0.5));
      float alpha = smoothstep(0.5, 0.3, dist) * vColor.a;
      if (alpha < 0.01) discard;
      FragColor = vec4(vColor.rgb, alpha);
    }
    """;

  // ── HUD (screen-space quads, colored) ──────────────────────────────
  public const string HudVert =
    """
    #version 330 core
    layout(location = 0) in vec2 aPos;
    layout(location = 1) in vec2 aTexCoord;
    layout(location = 2) in vec4 aColor;

    out vec2 vTexCoord;
    out vec4 vColor;

    void main()
    {
      vTexCoord = aTexCoord;
      vColor = aColor;
      gl_Position = vec4(aPos, 0.0, 1.0);
    }
    """;

  public const string HudFrag =
    """
    #version 330 core
    in vec2 vTexCoord;
    in vec4 vColor;
    out vec4 FragColor;

    uniform sampler2D uTexture;

    void main()
    {
      vec4 tex = texture(uTexture, vTexCoord);
      FragColor = vColor * tex;
    }
    """;
}
