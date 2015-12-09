﻿using System;
using System.Collections.Generic;

namespace Bit.Api.Models
{
    public class ListResponseModel<T> : ResponseModel where T : ResponseModel
    {
        public ListResponseModel(IEnumerable<T> data)
            : base("list")
        {
            Data = data;
        }

        public IEnumerable<T> Data { get; set; }
    }
}
