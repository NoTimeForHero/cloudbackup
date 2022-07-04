﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinClient
{
    class Config
    {
        public bool topmost { get; set; }
        public bool debug_mode { get; set; }
        public string urlWatch { get; set; }
        public string urlStartJob { get; set; }
        public TimeSpan? shutdown_computer { get; set; }

        /// <summary>
        /// Если true, то нет иконки в трее и кнопка закрытия закрывает всё приложение
        /// </summary>
        public bool no_background { get; set; }

        public bool exit_after_complete { get; set; }


        public static Config Default => new Config
        {
            topmost = true,
            urlWatch = "ws://localhost:3000/ws-status",
            urlStartJob = "http://localhost:3000/api/jobs/start/{0}",
            shutdown_computer = null
        };
    }
}
